// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Contracts.Hashing;
using BinStash.Core.Entities;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Storage;
using FluentAssertions;

namespace BinStash.Server.Tests.Storage;

/// <summary>
/// Behavioural tests for the online garbage collector's storage half.
///
/// <para>
/// These run against a real on-disk store with a deliberately tiny pack rotation size, because
/// the properties worth testing here are all about what happens across pack file boundaries:
/// which pack is safe to rewrite, whether a reader survives one being replaced, and whether the
/// index still resolves afterwards.
/// </para>
/// </summary>
public sealed class ObjectStoreGarbageCollectorTests : IDisposable
{
    /// <summary>
    /// Small enough that a handful of chunks spans several pack files, which is what lets these
    /// tests reach the compaction path at all — the pack currently being appended to is never
    /// rewritten, so a single-pack store has nothing to collect.
    /// </summary>
    private const long TinyPackSize = 512;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "binstash-gc-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* temp dir */ }
    }

    /// <summary>The ambient test cancellation token, so a hung run is interruptible.</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ObjectStore NewStore(long maxPackSize = TinyPackSize)
        => new(_root, maxOpenHandlers: 32, maxPackSize: maxPackSize);

    // -----------------------------------------------------------------------
    // Helpers

    private static Hash32 HashOf(byte[] payload) => new(Blake3.Hasher.Hash(payload).AsSpan());

    /// <summary>
    /// Finds <paramref name="count"/> distinct payloads whose hashes all land in one prefix
    /// bucket. Content addressing scatters chunks across 4096 buckets, and the collector works
    /// per bucket, so a test that wants several objects in the same bucket has to go looking.
    /// </summary>
    private static (string Prefix, List<byte[]> Payloads) PayloadsInOneBucket(int count, int payloadSize = 64)
    {
        var byPrefix = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);

        for (var i = 0; i < 500_000; i++)
        {
            var payload = new byte[payloadSize];
            BitConverter.TryWriteBytes(payload, i);
            // Fill the rest so the chunks do not compress down to identical sizes.
            for (var j = 4; j < payloadSize; j++)
                payload[j] = (byte)((i * 31 + j) & 0xFF);

            var prefix = HashOf(payload).ToHexString()[..3];

            if (!byPrefix.TryGetValue(prefix, out var list))
                byPrefix[prefix] = list = [];

            list.Add(payload);

            if (list.Count == count)
                return (prefix, list);
        }

        throw new InvalidOperationException($"Could not find {count} payloads sharing a prefix bucket.");
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task Watermark_excludes_objects_written_after_it_was_taken()
    {
        var (prefix, payloads) = PayloadsInOneBucket(4);
        await using var store = NewStore();

        await store.WriteChunkAsync(payloads[0]);
        await store.WriteChunkAsync(payloads[1]);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        // Written after the watermark: an ingest landing mid-run must be invisible to that run.
        await store.WriteChunkAsync(payloads[2]);
        await store.WriteChunkAsync(payloads[3]);

        var visible = new List<Hash32>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            visible.Add(entry.Hash);

        visible.Should().BeEquivalentTo([HashOf(payloads[0]), HashOf(payloads[1])]);
    }

    [Fact]
    public async Task Watermark_on_an_empty_bucket_yields_no_candidates()
    {
        var (prefix, payloads) = PayloadsInOneBucket(1);
        await using var store = NewStore();

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        await store.WriteChunkAsync(payloads[0]);

        var visible = new List<Hash32>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            visible.Add(entry.Hash);

        visible.Should().BeEmpty("nothing existed when the run started, so nothing it can see is collectable");
    }

    [Fact]
    public async Task Duplicate_write_reports_the_stored_size_so_a_stale_catalogue_can_heal()
    {
        var (_, payloads) = PayloadsInOneBucket(1);
        await using var store = NewStore();

        var first = await store.WriteChunkAsync(payloads[0]);
        var second = await store.WriteChunkAsync(payloads[0]);

        first.WasNew.Should().BeTrue();
        second.WasNew.Should().BeFalse();
        second.Length.Should().Be(first.Length,
            "a caller re-inserting a catalogue row for content that is already stored still needs its size");
    }

    [Fact]
    public async Task Reclaim_drops_doomed_objects_and_keeps_everything_else_readable()
    {
        var (prefix, payloads) = PayloadsInOneBucket(8);
        await using var store = NewStore();

        foreach (var payload in payloads)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        // Every pack but the one still being appended to is fair game.
        var appendPack = stored.Max(static s => s.FileNo);
        var doomed = stored.Where(s => s.FileNo < appendPack).Take(2).ToList();
        doomed.Should().NotBeEmpty("the tiny pack size should have rotated several sealed packs");

        var options = new GarbageCollectionOptions { MinimumPackGarbageRatio = 0 };
        var result = await collector.ReclaimAsync(bucket, doomed, options, Ct);

        result.ReclaimedHashes.Should().BeEquivalentTo(doomed.Select(static d => d.Hash));

        var doomedHashes = doomed.Select(static d => d.Hash).ToHashSet();

        foreach (var payload in payloads)
        {
            var hash = HashOf(payload);

            if (doomedHashes.Contains(hash))
            {
                var read = async () => await store.ReadChunkAsync(hash.ToHexString());
                await read.Should().ThrowAsync<KeyNotFoundException>("reclaimed content must stop resolving");
            }
            else
            {
                var read = await store.ReadChunkAsync(hash.ToHexString());
                read.Should().Equal(payload, "copy-forward compaction must not disturb surviving content");
            }
        }
    }

    [Fact]
    public async Task Reclaim_defers_objects_in_the_pack_still_being_appended_to()
    {
        var (prefix, payloads) = PayloadsInOneBucket(4);

        // One huge pack: everything lands in the file the writer is still appending to.
        await using var store = NewStore(maxPackSize: 16 * 1024 * 1024);

        foreach (var payload in payloads)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        var result = await collector.ReclaimAsync(bucket, stored, new GarbageCollectionOptions { MinimumPackGarbageRatio = 0 }, Ct);

        result.ReclaimedHashes.Should().BeEmpty();
        result.DeferredObjects.Should().Be(stored.Count,
            "rewriting the pack the writer is appending to would race the writer, so it waits for a later run");

        foreach (var payload in payloads)
            (await store.ReadChunkAsync(HashOf(payload).ToHexString())).Should().Equal(payload);
    }

    [Fact]
    public async Task Reclaim_leaves_a_pack_alone_until_enough_of_it_is_dead()
    {
        var (prefix, payloads) = PayloadsInOneBucket(8);
        await using var store = NewStore();

        foreach (var payload in payloads)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        var appendPack = stored.Max(static s => s.FileNo);
        var doomed = stored.Where(s => s.FileNo < appendPack).Take(1).ToList();

        // A ratio no partially-dead pack can reach: rewriting would cost more I/O than it frees.
        var result = await collector.ReclaimAsync(bucket, doomed, new GarbageCollectionOptions { MinimumPackGarbageRatio = 1.1 }, Ct);

        result.ReclaimedHashes.Should().BeEmpty();
        result.DeferredObjects.Should().Be(doomed.Count);
    }

    [Fact]
    public async Task Superseded_packs_survive_a_drain_window_and_are_then_deleted()
    {
        var (prefix, payloads) = PayloadsInOneBucket(8);
        await using var store = NewStore();

        foreach (var payload in payloads)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        var appendPack = stored.Max(static s => s.FileNo);
        var doomed = stored.Where(s => s.FileNo < appendPack).ToList();
        doomed.Should().NotBeEmpty();

        await collector.ReclaimAsync(bucket, doomed, new GarbageCollectionOptions { MinimumPackGarbageRatio = 0 }, Ct);

        var packCountAfterReclaim = CountPackFiles();

        // A reader may still be holding an address into a superseded pack, so nothing is
        // unlinked until the drain window has passed.
        var early = await collector.PurgeObsoletePacksAsync(TimeSpan.FromHours(1), Ct);
        early.PacksDeleted.Should().Be(0);
        early.PacksStillDraining.Should().BeGreaterThan(0);
        CountPackFiles().Should().Be(packCountAfterReclaim);

        var late = await collector.PurgeObsoletePacksAsync(TimeSpan.Zero, Ct);
        late.PacksDeleted.Should().BeGreaterThan(0);
        late.BytesFreed.Should().BeGreaterThan(0);
        CountPackFiles().Should().BeLessThan(packCountAfterReclaim);

        // Purging is self-validating rather than marker-driven, so repeating it is a no-op.
        var again = await collector.PurgeObsoletePacksAsync(TimeSpan.Zero, Ct);
        again.PacksDeleted.Should().Be(0);

        var survivors = payloads.Where(p => !doomed.Select(static d => d.Hash).Contains(HashOf(p)));
        foreach (var payload in survivors)
            (await store.ReadChunkAsync(HashOf(payload).ToHexString())).Should().Equal(payload);
    }

    [Fact]
    public async Task Reads_and_writes_continue_correctly_while_a_bucket_is_being_reclaimed()
    {
        var (prefix, payloads) = PayloadsInOneBucket(24);
        await using var store = NewStore();

        var initial = payloads.Take(16).ToList();
        foreach (var payload in initial)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        var appendPack = stored.Max(static s => s.FileNo);
        var doomedHashes = stored.Where(s => s.FileNo < appendPack).Take(4).Select(static s => s.Hash).ToHashSet();
        var doomed = stored.Where(s => doomedHashes.Contains(s.Hash)).ToList();
        doomed.Should().NotBeEmpty();

        var survivors = initial.Where(p => !doomedHashes.Contains(HashOf(p))).ToList();
        var newcomers = payloads.Skip(16).ToList();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Hammer the bucket from both sides while the collector rewrites pack files underneath.
        var readerFailure = null as Exception;
        var reader = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (var payload in survivors)
                    {
                        var read = await store.ReadChunkAsync(HashOf(payload).ToHexString());
                        if (!read.SequenceEqual(payload))
                            throw new InvalidOperationException("A concurrent read observed corrupted content.");
                    }
                }
            }
            catch (Exception ex)
            {
                readerFailure = ex;
            }
        }, Ct);

        var writer = Task.Run(async () =>
        {
            foreach (var payload in newcomers)
                await store.WriteChunkAsync(payload);
        }, Ct);

        var result = await collector.ReclaimAsync(bucket, doomed, new GarbageCollectionOptions { MinimumPackGarbageRatio = 0 }, Ct);

        await writer;
        await stop.CancelAsync();
        await reader;

        readerFailure.Should().BeNull("reads must never observe a torn or missing pack entry during reclaim");
        result.ReclaimedHashes.Should().NotBeEmpty();

        foreach (var payload in survivors.Concat(newcomers))
            (await store.ReadChunkAsync(HashOf(payload).ToHexString())).Should().Equal(payload);

        foreach (var hash in doomedHashes)
        {
            var read = async () => await store.ReadChunkAsync(hash.ToHexString());
            await read.Should().ThrowAsync<KeyNotFoundException>();
        }
    }

    [Fact]
    public async Task Reclaimed_content_can_be_written_again_from_scratch()
    {
        var (prefix, payloads) = PayloadsInOneBucket(8);
        await using var store = NewStore();

        foreach (var payload in payloads)
            await store.WriteChunkAsync(payload);

        var collector = store.GarbageCollector;
        var bucket = new GcBucketId(GcObjectCategory.Chunk, prefix);
        var watermark = await collector.SnapshotWatermarkAsync(bucket, Ct);

        var stored = new List<GcObjectRef>();
        await foreach (var entry in collector.EnumerateBucketAsync(bucket, watermark, Ct))
            stored.Add(entry);

        var appendPack = stored.Max(static s => s.FileNo);
        var doomed = stored.Where(s => s.FileNo < appendPack).Take(1).ToList();
        doomed.Should().NotBeEmpty();

        await collector.ReclaimAsync(bucket, doomed, new GarbageCollectionOptions { MinimumPackGarbageRatio = 0 }, Ct);

        // This is the recovery path a client takes after being told the content is missing.
        var revived = payloads.Single(p => HashOf(p) == doomed[0].Hash);
        var rewrite = await store.WriteChunkAsync(revived);

        rewrite.WasNew.Should().BeTrue();
        (await store.ReadChunkAsync(doomed[0].Hash.ToHexString())).Should().Equal(revived);
    }

    private int CountPackFiles()
        => Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "*.pack", SearchOption.AllDirectories).Count()
            : 0;
}
