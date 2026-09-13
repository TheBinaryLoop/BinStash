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
using FluentAssertions;

namespace BinStash.Core.Tests.Storage;

/// <summary>
/// Tests for the spill-to-disk reachable set used by the garbage-collection mark phase.
///
/// <para>
/// The property that matters is total recall: a hash that is added must come back. A mark that
/// goes missing is not a slow collection, it is deleted data, so these tests push enough hashes
/// through to cross the internal flush thresholds rather than staying in the buffered fast path.
/// </para>
/// </summary>
public sealed class GcMarkSetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "binstash-markset-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* temp dir */ }
    }

    private static Hash32 HashOf(int seed)
    {
        Span<byte> bytes = stackalloc byte[Hash32.Size];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((seed * 2654435761L >> (i % 4 * 8)) ^ (i * 37));
        return new Hash32(bytes);
    }

    private static string PrefixOf(Hash32 hash) => hash.ToHexString()[..3];

    [Fact]
    public void Every_added_hash_comes_back_from_its_bucket()
    {
        // Comfortably past both the per-bucket and the global flush thresholds, so the set is
        // exercised on disk rather than purely in memory.
        const int count = 50_000;

        var expected = new Dictionary<string, HashSet<Hash32>>(StringComparer.Ordinal);

        using (var marks = new GcMarkSet(_root))
        {
            for (var i = 0; i < count; i++)
            {
                var hash = HashOf(i);
                marks.Add(GcObjectCategory.Chunk, hash);

                if (!expected.TryGetValue(PrefixOf(hash), out var set))
                    expected[PrefixOf(hash)] = set = [];
                set.Add(hash);
            }

            marks.Flush();

            foreach (var (prefix, hashes) in expected)
            {
                var loaded = marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, prefix));
                loaded.Should().BeEquivalentTo(hashes);
            }
        }
    }

    [Fact]
    public void Repeated_marks_collapse_on_load()
    {
        using var marks = new GcMarkSet(_root);

        var hash = HashOf(7);
        for (var i = 0; i < 10_000; i++)
            marks.Add(GcObjectCategory.Chunk, hash);

        marks.Flush();

        var loaded = marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, PrefixOf(hash)));
        loaded.Should().ContainSingle().Which.Should().Be(hash);
    }

    [Fact]
    public void Categories_do_not_bleed_into_each_other()
    {
        using var marks = new GcMarkSet(_root);

        var chunk = HashOf(1);
        var fileDef = HashOf(2);

        marks.Add(GcObjectCategory.Chunk, chunk);
        marks.Add(GcObjectCategory.FileDefinition, fileDef);
        marks.Flush();

        marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, PrefixOf(chunk)))
            .Should().ContainSingle().Which.Should().Be(chunk);

        marks.LoadBucket(new GcBucketId(GcObjectCategory.FileDefinition, PrefixOf(fileDef)))
            .Should().ContainSingle().Which.Should().Be(fileDef);

        marks.LoadBucket(new GcBucketId(GcObjectCategory.FileDefinition, PrefixOf(chunk)))
            .Should().BeEmpty();
    }

    [Fact]
    public void An_unmarked_bucket_loads_as_empty()
    {
        using var marks = new GcMarkSet(_root);
        marks.Add(GcObjectCategory.Chunk, HashOf(1));
        marks.Flush();

        // This is the case the sweep depends on: a bucket that holds data but received no marks
        // is entirely unreachable, and must not be mistaken for "not yet computed".
        marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, "fff")).Should().BeEmpty();
    }

    [Fact]
    public void Marked_prefixes_lists_only_buckets_that_received_marks()
    {
        using var marks = new GcMarkSet(_root);

        var hashes = Enumerable.Range(0, 200).Select(HashOf).ToList();
        foreach (var hash in hashes)
            marks.Add(GcObjectCategory.FileDefinition, hash);

        marks.Flush();

        marks.MarkedPrefixes(GcObjectCategory.FileDefinition)
            .Should().BeEquivalentTo(hashes.Select(PrefixOf).Distinct());

        marks.MarkedPrefixes(GcObjectCategory.Chunk).Should().BeEmpty();
    }

    [Fact]
    public void Marks_added_concurrently_are_all_retained()
    {
        const int perTask = 5_000;
        const int tasks = 8;

        using var marks = new GcMarkSet(_root);

        Parallel.For(0, tasks, t =>
        {
            for (var i = 0; i < perTask; i++)
                marks.Add(GcObjectCategory.Chunk, HashOf(t * perTask + i));
        });

        marks.Flush();

        var recovered = marks.MarkedPrefixes(GcObjectCategory.Chunk)
            .Sum(prefix => marks.LoadBucket(new GcBucketId(GcObjectCategory.Chunk, prefix)).Count);

        recovered.Should().Be(tasks * perTask, "the mark phase runs several walks at once and may not drop any of them");
    }

    [Fact]
    public void Deleting_spill_files_is_safe_to_repeat()
    {
        var marks = new GcMarkSet(_root);
        marks.Add(GcObjectCategory.Chunk, HashOf(1));
        marks.Flush();

        Directory.Exists(_root).Should().BeTrue();

        marks.DeleteSpillFiles();
        marks.DeleteSpillFiles();

        Directory.Exists(_root).Should().BeFalse();
    }
}
