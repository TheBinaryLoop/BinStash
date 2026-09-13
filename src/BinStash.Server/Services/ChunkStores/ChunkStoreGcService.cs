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

using System.Text.Json;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Data;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Server.Services.ReleaseUpgrade;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Services.ChunkStores;

/// <summary>
/// Raised when a run cannot establish what is reachable and must stop without collecting.
/// </summary>
public sealed class GcAbortedException : Exception
{
    public GcAbortedException(string message) : base(message) { }
}

/// <summary>
/// Online mark-and-sweep garbage collection for a chunk store.
///
/// <para>
/// The store is content-addressed and deduplicated across tenants, so nothing local tells you
/// whether a chunk is still needed — only a walk of every release that could reach it does.
/// That walk necessarily races the ingest traffic it is walking. Three mechanisms, none of
/// which requires taking the store offline or holding a global lock, make the race safe:
/// </para>
/// <list type="number">
///   <item>
///     <strong>Watermarks.</strong> Each bucket's append position is captured before marking
///     starts. Anything stored at or beyond it is newer than the run's view of the world and is
///     never a candidate, so an ingest that lands mid-run cannot be collected by it.
///   </item>
///   <item>
///     <strong>Reversible quarantine.</strong> The sweep does not delete; it moves objects to a
///     tombstone that hides them from deduplication while leaving the bytes in place. New
///     ingests re-upload; in-flight ingests are repaired at finalize by
///     <see cref="IGcQuarantineService"/>. Bytes are dropped only after a retention window
///     during which every decision remains reversible.
///   </item>
///   <item>
///     <strong>Copy-forward reclaim.</strong> Pack files are never rewritten in place. Live
///     entries are copied to a new pack and the superseded file is retired, so a reader that
///     already resolved an address keeps reading valid bytes.
///   </item>
/// </list>
/// <para>
/// The design bias throughout is that reclaiming late costs disk and reclaiming early costs
/// data. Every ambiguous case is therefore resolved by deferring to the next run.
/// </para>
/// </summary>
public sealed class ChunkStoreGcService : IChunkStoreGcService
{
    /// <summary>Buckets processed between progress persists / subscription broadcasts.</summary>
    private const int BroadcastEveryBuckets = 128;

    /// <summary>Releases marked between progress persists / subscription broadcasts.</summary>
    private const int BroadcastEveryReleases = 25;

    /// <summary>Tombstones inserted or deleted per database round trip.</summary>
    private const int DbBatchSize = 1000;

    /// <summary>
    /// Cap on distinct warnings recorded against a run. A store-wide problem produces the same
    /// warning once per bucket, and a thousand copies of one line is not more informative.
    /// </summary>
    private const int MaxReportedWarnings = 20;

    private readonly IServiceProvider _services;
    private readonly IOptions<GarbageCollectionOptions> _options;
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<ChunkStoreGcService> _logger;

    public ChunkStoreGcService(
        IServiceProvider services,
        IOptions<GarbageCollectionOptions> options,
        ITopicEventSender eventSender,
        ILogger<ChunkStoreGcService> logger)
    {
        _services = services;
        _options = options;
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var storageFactory = scope.ServiceProvider.GetRequiredService<IChunkStoreStorageFactory>();

        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogError("GC job {JobId} not found", jobId);
            return;
        }

        if (job.Status is BackgroundJobStatus.Cancelled)
        {
            _logger.LogInformation("GC job {JobId} was cancelled before starting", jobId);
            return;
        }

        var jobData = JsonSerializer.Deserialize<ChunkStoreGcJobData>(job.JobData ?? "{}");
        var progress = new ChunkStoreGcProgressData();

        if (jobData is null)
        {
            await FailAsync(db, job, null, progress, "Invalid JobData payload.", cancellationToken);
            return;
        }

        var store = await db.ChunkStores.FindAsync([jobData.ChunkStoreId], cancellationToken);
        if (store is null)
        {
            await FailAsync(db, job, jobData, progress, $"ChunkStore {jobData.ChunkStoreId} not found.", cancellationToken);
            return;
        }

        var storage = storageFactory.Create(store);
        var collector = storage.GarbageCollector;
        if (collector is null)
        {
            await FailAsync(db, job, jobData, progress,
                $"Chunk store backend '{store.Type}' does not support online garbage collection.", cancellationToken);
            return;
        }

        var options = BuildOptions(jobData);
        var runId = job.Id;
        var startedAt = DateTimeOffset.UtcNow;

        var spillRoot = System.IO.Path.Combine(collector.WorkingDirectory, $"run-{runId:N}");
        using var marks = new GcMarkSet(spillRoot);

        job.Status = BackgroundJobStatus.Running;
        job.StartedAt = startedAt;
        progress.Phase = ChunkStoreGcPhases.Snapshot;
        job.ProgressData = JsonSerializer.Serialize(progress);
        await db.SaveChangesAsync(cancellationToken);
        await BroadcastAsync(job, jobData, progress, cancellationToken);

        var ctx = new GcRunContext(db, job, jobData, progress, store.Id, storage, collector, options, runId, startedAt);

        try
        {
            var watermarks = await SnapshotWatermarksAsync(ctx, cancellationToken);

            ctx.Progress.Phase = ChunkStoreGcPhases.Mark;
            var releaseHashes = await MarkAsync(ctx, marks, cancellationToken);
            marks.Flush();

            ctx.Progress.Phase = ChunkStoreGcPhases.Sweep;
            await SweepAsync(ctx, marks, watermarks, cancellationToken);
            await SweepReleasePackagesAsync(ctx, releaseHashes, cancellationToken);

            if (!options.SkipReclaim && !options.DryRun)
            {
                ctx.Progress.Phase = ChunkStoreGcPhases.Reclaim;
                await ReclaimAsync(ctx, cancellationToken);
            }

            progress.Phase = ChunkStoreGcPhases.Completed;
            job.Status = BackgroundJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastAsync(job, jobData, progress, CancellationToken.None);

            _logger.LogInformation(
                "GC job {JobId} completed for chunk store {ChunkStoreId}: quarantined {Quarantined} object(s) ({QuarantinedBytes} B), " +
                "reclaimed {Reclaimed} object(s) ({ReclaimedBytes} B of dead entries), compacted {Packs} pack(s), " +
                "deleted {Deleted} pack file(s) returning {PackBytesDeleted} B to the filesystem",
                jobId, store.Id, progress.QuarantinedObjects, progress.QuarantinedBytes,
                progress.ReclaimedObjects, progress.ReclaimedBytes, progress.PacksCompacted,
                progress.PacksDeleted, progress.PackBytesDeleted);
        }
        catch (OperationCanceledException)
        {
            job.Status = BackgroundJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastAsync(job, jobData, progress, CancellationToken.None);
            _logger.LogInformation("GC job {JobId} cancelled during phase {Phase}", jobId, progress.Phase);
        }
        catch (GcAbortedException ex)
        {
            // A deliberate abort: the run could not establish what is reachable, so continuing
            // would mean guessing. Nothing has been quarantined or deleted at this point.
            await FailAsync(db, job, jobData, progress, ex.Message, CancellationToken.None);
            _logger.LogError("GC job {JobId} aborted without collecting: {Reason}", jobId, ex.Message);
        }
        catch (Exception ex)
        {
            await FailAsync(db, job, jobData, progress, ex.ToString(), CancellationToken.None);
            _logger.LogError(ex, "GC job {JobId} failed with an unhandled exception", jobId);
        }
        finally
        {
            marks.DeleteSpillFiles();
        }
    }

    /// <summary>Everything a phase needs, so the phase signatures stay readable.</summary>
    private sealed record GcRunContext(
        BinStashDbContext Db,
        BackgroundJob Job,
        ChunkStoreGcJobData JobData,
        ChunkStoreGcProgressData Progress,
        Guid ChunkStoreId,
        IChunkStoreStorage Storage,
        IChunkStoreGarbageCollector Collector,
        GarbageCollectionOptions Options,
        Guid RunId,
        DateTimeOffset StartedAt);

    private GarbageCollectionOptions BuildOptions(ChunkStoreGcJobData jobData)
    {
        var configured = _options.Value;

        var options = new GarbageCollectionOptions
        {
            RetentionWindow = jobData.RetentionHoursOverride is { } hours
                ? TimeSpan.FromHours(hours)
                : configured.RetentionWindow,
            ObsoletePackDrainWindow = configured.ObsoletePackDrainWindow,
            MinimumPackGarbageRatio = configured.MinimumPackGarbageRatio,
            MaxPacksToCompactPerRun = configured.MaxPacksToCompactPerRun,
            BucketConcurrency = configured.BucketConcurrency,
            DryRun = jobData.DryRun,
            SkipReclaim = jobData.SkipReclaim
        };

        options.Validate();
        return options;
    }

    // =======================================================================
    // Phase 1 — watermark

    /// <summary>
    /// Captures every live bucket's append position before anything is read.
    ///
    /// <para>
    /// This has to happen first and for all buckets. A watermark taken after marking began
    /// would admit objects the mark phase never had a chance to see, which is exactly the
    /// window an ingest running alongside the collector lands in.
    /// </para>
    /// </summary>
    private async Task<Dictionary<GcBucketId, GcBucketWatermark>> SnapshotWatermarksAsync(
        GcRunContext ctx, CancellationToken ct)
    {
        var buckets = EnumerateLiveBuckets(ctx.Collector).ToArray();
        var watermarks = new Dictionary<GcBucketId, GcBucketWatermark>(buckets.Length);

        ctx.Progress.TotalBuckets = buckets.Length;

        foreach (var bucket in buckets)
        {
            ct.ThrowIfCancellationRequested();
            watermarks[bucket] = await ctx.Collector.SnapshotWatermarkAsync(bucket, ct);
        }

        await PersistProgressAsync(ctx, ct);
        return watermarks;
    }

    private static IEnumerable<GcBucketId> EnumerateLiveBuckets(IChunkStoreGarbageCollector collector)
    {
        foreach (var category in new[] { GcObjectCategory.FileDefinition, GcObjectCategory.Chunk })
        {
            foreach (var prefix in collector.BucketPrefixes)
            {
                var bucket = new GcBucketId(category, prefix);
                if (collector.BucketHasData(bucket))
                    yield return bucket;
            }
        }
    }

    // =======================================================================
    // Phase 2 — mark

    /// <summary>
    /// Walks every release on the store to its file definitions, and every marked file
    /// definition to its chunks.
    ///
    /// <para>
    /// The walk is two passes rather than one for a reason of scale. Marking chunks directly
    /// per release would append each shared chunk once per release that contains it, and
    /// releases of the same product overlap almost entirely — the spill would be quadratic in
    /// release count. Deduplicating at the file-definition level first collapses that to
    /// roughly one emission per distinct file, which is proportional to the store rather than
    /// to its history.
    /// </para>
    /// </summary>
    /// <returns>The set of release-package hashes that are still referenced by a release row.</returns>
    private async Task<HashSet<string>> MarkAsync(GcRunContext ctx, GcMarkSet marks, CancellationToken ct)
    {
        var releases = await ctx.Db.Releases
            .Where(r => r.Repository.ChunkStoreId == ctx.ChunkStoreId)
            .Select(r => new ReleaseRoot(r.Id, r.Version, r.ReleaseDefinitionChecksum))
            .ToListAsync(ct);

        ctx.Progress.TotalReleases = releases.Count;
        var referencedPackages = new HashSet<string>(releases.Count, StringComparer.OrdinalIgnoreCase);

        // ---- Pass 1: releases -> file definitions -------------------------
        foreach (var release in releases)
        {
            ct.ThrowIfCancellationRequested();

            var packageHash = release.DefinitionChecksum.ToHexString();
            referencedPackages.Add(packageHash);

            byte[]? packageBytes;
            try
            {
                packageBytes = await ctx.Storage.RetrieveReleasePackageAsync(packageHash);
            }
            catch (Exception ex)
            {
                throw new GcAbortedException(
                    $"Release '{release.Version}' ({release.Id}) references release package {packageHash}, " +
                    $"which could not be read ({ex.GetType().Name}: {ex.Message}). Refusing to collect: every " +
                    "object that release reaches would be treated as unreachable. Repair or remove the release first.");
            }

            if (packageBytes is null || packageBytes.Length == 0)
            {
                throw new GcAbortedException(
                    $"Release '{release.Version}' ({release.Id}) references release package {packageHash}, " +
                    "which is missing from the store. Refusing to collect: every object that release reaches " +
                    "would be treated as unreachable. Repair or remove the release first.");
            }

            ReleasePackage package;
            try
            {
                (package, _) = await ReleasePackageSerializer.DeserializeAsync(packageBytes, ct);
            }
            catch (Exception ex)
            {
                throw new GcAbortedException(
                    $"Release package {packageHash} for release '{release.Version}' ({release.Id}) could not be " +
                    $"deserialized ({ex.GetType().Name}: {ex.Message}). Refusing to collect.");
            }

            if (package.PackageFormatVersion == 5)
            {
                // V5 addresses file definitions by StorageKey, an identity the store no longer
                // carries. Marking it would look successful and mark nothing.
                throw new GcAbortedException(
                    $"Release '{release.Version}' ({release.Id}) is stored in the legacy V5 format, whose file " +
                    "references cannot be resolved against the current file-definition index. Run the release " +
                    "upgrade job on this chunk store before collecting.");
            }

            foreach (var fileHash in CollectReferencedFileHashes(package))
                marks.Add(GcObjectCategory.FileDefinition, fileHash);

            ctx.Progress.MarkedReleases++;

            if (ctx.Progress.MarkedReleases % BroadcastEveryReleases == 0)
            {
                marks.Flush();
                await PersistProgressAsync(ctx, ct);
            }
        }

        marks.Flush();

        // ---- Pass 2: file definitions -> chunks ---------------------------
        foreach (var prefix in marks.MarkedPrefixes(GcObjectCategory.FileDefinition))
        {
            ct.ThrowIfCancellationRequested();

            var bucket = new GcBucketId(GcObjectCategory.FileDefinition, prefix);
            var fileHashes = marks.LoadBucket(bucket);

            foreach (var fileHash in fileHashes)
            {
                ct.ThrowIfCancellationRequested();

                var blob = await ctx.Collector.ReadFileDefinitionForMarkAsync(fileHash, ct);
                if (blob is null)
                {
                    // Reachable from a release but not present. The release is already broken;
                    // saying so is more useful than quietly collecting the rest of its chunks.
                    throw new GcAbortedException(
                        $"File definition {fileHash.ToHexString()} is referenced by a release but is missing from " +
                        "the store. Refusing to collect: its chunks would be treated as unreachable. Rebuild the " +
                        "chunk store index, or repair the affected release, and try again.");
                }

                FileDefinitionRecord record;
                try
                {
                    record = FileDefinitionRecord.Deserialize(blob);
                }
                catch (Exception ex)
                {
                    throw new GcAbortedException(
                        $"File definition {fileHash.ToHexString()} could not be decoded " +
                        $"({ex.GetType().Name}: {ex.Message}). Refusing to collect.");
                }

                foreach (var chunkHash in record.ChunkHashes)
                    marks.Add(GcObjectCategory.Chunk, chunkHash);
            }

            marks.Flush();
        }

        await PersistProgressAsync(ctx, ct);
        return referencedPackages;
    }

    private readonly record struct ReleaseRoot(Guid Id, string Version, Hash32 DefinitionChecksum);

    /// <summary>
    /// Every file-content hash a release package reaches, across both artifact backings.
    /// A backing the serializer does not recognise aborts the run rather than contributing
    /// nothing, because "no hashes" and "no hashes I understood" are indistinguishable to a
    /// sweep and only one of them is safe.
    /// </summary>
    private static IEnumerable<Hash32> CollectReferencedFileHashes(ReleasePackage package)
    {
        foreach (var artifact in package.OutputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    if (opaque.ContentHash is null)
                        throw new GcAbortedException($"Output artifact '{artifact.Path}' has no content hash. Refusing to collect.");
                    yield return opaque.ContentHash.Value;
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash is null)
                            throw new GcAbortedException($"Container member '{member.EntryPath}' of '{artifact.Path}' has no content hash. Refusing to collect.");
                        yield return member.ContentHash.Value;
                    }
                    break;

                default:
                    throw new GcAbortedException(
                        $"Output artifact '{artifact.Path}' uses backing type '{artifact.Backing.GetType().Name}', which this " +
                        "server does not know how to walk. Refusing to collect.");
            }
        }
    }

    // =======================================================================
    // Phase 3 — sweep (quarantine)

    /// <summary>
    /// Quarantines every stored object the mark phase did not reach.
    ///
    /// <para>
    /// Nothing is deleted here. Each candidate gets a tombstone and loses its catalogue row in
    /// one transaction, which hides it from deduplication while its bytes stay readable. That
    /// is what makes the decision reversible: a release that turns out to need the object
    /// resurrects it, and an ingest that asks for it afterwards is simply told to upload it
    /// again.
    /// </para>
    /// </summary>
    private async Task SweepAsync(
        GcRunContext ctx,
        GcMarkSet marks,
        Dictionary<GcBucketId, GcBucketWatermark> watermarks,
        CancellationToken ct)
    {
        var processed = 0;

        foreach (var (bucket, watermark) in watermarks)
        {
            ct.ThrowIfCancellationRequested();

            var marked = marks.LoadBucket(bucket);
            ctx.Progress.ReachableObjects += marked.Count;

            var candidates = new List<GcObjectRef>();

            await foreach (var stored in ctx.Collector.EnumerateBucketAsync(bucket, watermark, ct))
            {
                if (!marked.Contains(stored.Hash))
                    candidates.Add(stored);
            }

            if (candidates.Count > 0)
            {
                if (ctx.Options.DryRun)
                {
                    ctx.Progress.QuarantinedObjects += candidates.Count;
                    ctx.Progress.QuarantinedBytes += candidates.Sum(static c => (long)c.Length);
                }
                else
                {
                    await QuarantineAsync(ctx, bucket, candidates, ct);
                }
            }

            ctx.Progress.ProcessedBuckets = ++processed;

            if (processed % BroadcastEveryBuckets == 0)
                await PersistProgressAsync(ctx, ct);
        }

        await PersistProgressAsync(ctx, ct);
    }

    private async Task QuarantineAsync(
        GcRunContext ctx, GcBucketId bucket, List<GcObjectRef> candidates, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var eligibleAt = now + ctx.Options.RetentionWindow;

        foreach (var batch in candidates.Chunk(DbBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var hashes = batch.Select(static c => c.Hash).ToList();

            // Objects quarantined by an earlier run keep their original clock. Refreshing it
            // would mean a store collected on a schedule shorter than the retention window
            // could never reach the reclaim phase at all.
            var alreadyQuarantined = await ctx.Db.ChunkStoreGcTombstones
                .Where(t => t.ChunkStoreId == ctx.ChunkStoreId && t.Category == bucket.Category && hashes.Contains(t.Checksum))
                .Select(t => t.Checksum)
                .ToHashSetAsync(ct);

            var lengths = bucket.Category == GcObjectCategory.Chunk
                ? await ctx.Db.Chunks
                    .Where(c => c.ChunkStoreId == ctx.ChunkStoreId && hashes.Contains(c.Checksum))
                    .ToDictionaryAsync(c => c.Checksum, c => ((long)c.Length, c.CompressedLength), ct)
                : await ctx.Db.FileDefinitions
                    .Where(f => f.ChunkStoreId == ctx.ChunkStoreId && hashes.Contains(f.Checksum))
                    .ToDictionaryAsync(f => f.Checksum, f => (f.Length, 0), ct);

            var newTombstones = new List<ChunkStoreGcTombstone>(batch.Length);

            foreach (var candidate in batch)
            {
                if (alreadyQuarantined.Contains(candidate.Hash))
                    continue;

                // A candidate with no catalogue row is a pure orphan: bytes written by an ingest
                // whose database write never landed. Recording zero lengths is correct — there is
                // no row to restore, and resurrection knows not to invent one.
                var (logicalLength, compressedLength) = lengths.TryGetValue(candidate.Hash, out var l) ? l : (0L, 0);

                newTombstones.Add(new ChunkStoreGcTombstone
                {
                    ChunkStoreId = ctx.ChunkStoreId,
                    Category = bucket.Category,
                    Checksum = candidate.Hash,
                    BucketPrefix = bucket.Prefix,
                    RunId = ctx.RunId,
                    QuarantinedAt = now,
                    EligibleAt = eligibleAt,
                    PackFileNo = candidate.FileNo,
                    PackOffset = candidate.Offset,
                    PackLength = candidate.Length,
                    LogicalLength = logicalLength,
                    CompressedLength = compressedLength
                });

                ctx.Progress.QuarantinedObjects++;
                ctx.Progress.QuarantinedBytes += candidate.Length;
            }

            if (newTombstones.Count == 0)
                continue;

            // Order matters, because these are two separate statements and the process can die
            // between them. Tombstone first: a crash then leaves an object that is both tombstoned
            // and live, which reclaim already refuses to touch and the next run tidies up. The
            // other order would leave an object with neither a catalogue row nor a tombstone —
            // bytes nobody can find and nobody will ever reclaim.
            ctx.Db.ChunkStoreGcTombstones.AddRange(newTombstones);
            await ctx.Db.SaveChangesAsync(ct);
            ctx.Db.ChangeTracker.Clear();

            // Dropping the catalogue row is what actually quarantines the object: it is the row,
            // not the bytes, that the "which of these do you already have?" path consults.
            var toHide = newTombstones.Select(static t => t.Checksum).ToList();

            if (bucket.Category == GcObjectCategory.Chunk)
            {
                await ctx.Db.Chunks
                    .Where(c => c.ChunkStoreId == ctx.ChunkStoreId && toHide.Contains(c.Checksum))
                    .ExecuteDeleteAsync(ct);
            }
            else
            {
                await ctx.Db.FileDefinitions
                    .Where(f => f.ChunkStoreId == ctx.ChunkStoreId && toHide.Contains(f.Checksum))
                    .ExecuteDeleteAsync(ct);
            }
        }
    }

    /// <summary>
    /// Deletes release-definition packages that no release row names.
    ///
    /// <para>
    /// These are ordinary files rather than pack entries, and they are orphaned by a different
    /// mechanism than chunks: a release upgrade rewrites a package and removes the original, so
    /// a crash between those steps strands one. The retention window does double duty here — it
    /// keeps a package written moments ago, by an ingest whose release row has not committed
    /// yet, out of reach.
    /// </para>
    /// </summary>
    private async Task SweepReleasePackagesAsync(GcRunContext ctx, HashSet<string> referenced, CancellationToken ct)
    {
        var cutoff = (ctx.StartedAt - ctx.Options.RetentionWindow).UtcDateTime;

        await foreach (var package in ctx.Collector.EnumerateReleasePackagesAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            if (referenced.Contains(package.HashHex) || package.LastWriteUtc > cutoff)
                continue;

            ctx.Progress.QuarantinedObjects++;
            ctx.Progress.QuarantinedBytes += package.Bytes;

            if (ctx.Options.DryRun)
                continue;

            // Release packages have no deduplication path to hide from, so there is nothing for a
            // quarantine to buy: age plus "no release row names it" is already the full test.
            if (await ctx.Collector.DeleteReleasePackageAsync(package.HashHex, ct))
            {
                ctx.Progress.ReclaimedObjects++;
                ctx.Progress.ReclaimedBytes += package.Bytes;

                // Unlike a pack entry, a release package is its own file: dropping it returns the
                // space immediately, with no compaction or drain in between.
                ctx.Progress.PackBytesDeleted += package.Bytes;
            }
        }
    }

    // =======================================================================
    // Phase 4 — reclaim

    /// <summary>
    /// Physically drops objects whose quarantine has expired, then retires the pack files that
    /// copy-forward compaction superseded.
    ///
    /// <para>
    /// This is the only irreversible step in the whole design, so it is gated twice. The
    /// retention window covers the ordinary case; the ingest barrier covers the one it cannot.
    /// See <see cref="ComputeReclaimBarrierAsync"/>.
    /// </para>
    /// </summary>
    private async Task ReclaimAsync(GcRunContext ctx, CancellationToken ct)
    {
        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var barrier = await ComputeReclaimBarrierAsync(ctx, now, ct);

        var buckets = await ctx.Db.ChunkStoreGcTombstones
            .Where(t => t.ChunkStoreId == ctx.ChunkStoreId && t.EligibleAt <= now && t.QuarantinedAt < barrier)
            .Select(t => new { t.Category, t.BucketPrefix })
            .Distinct()
            .ToListAsync(ct);

        foreach (var bucketKey in buckets)
        {
            ct.ThrowIfCancellationRequested();

            var bucket = new GcBucketId(bucketKey.Category, bucketKey.BucketPrefix);

            var tombstones = await ctx.Db.ChunkStoreGcTombstones
                .Where(t => t.ChunkStoreId == ctx.ChunkStoreId
                            && t.Category == bucketKey.Category
                            && t.BucketPrefix == bucketKey.BucketPrefix
                            && t.EligibleAt <= now
                            && t.QuarantinedAt < barrier)
                .ToListAsync(ct);

            if (tombstones.Count == 0)
                continue;

            // Belt and braces against a resurrection that raced this query: a live catalogue row
            // means something depends on the object, whatever its tombstone says.
            var hashes = tombstones.Select(static t => t.Checksum).ToList();
            var live = bucketKey.Category == GcObjectCategory.Chunk
                ? await ctx.Db.Chunks
                    .Where(c => c.ChunkStoreId == ctx.ChunkStoreId && hashes.Contains(c.Checksum))
                    .Select(c => c.Checksum)
                    .ToHashSetAsync(ct)
                : await ctx.Db.FileDefinitions
                    .Where(f => f.ChunkStoreId == ctx.ChunkStoreId && hashes.Contains(f.Checksum))
                    .Select(f => f.Checksum)
                    .ToHashSetAsync(ct);

            var doomed = tombstones
                .Where(t => !live.Contains(t.Checksum))
                .Select(static t => new GcObjectRef(t.Checksum, t.PackFileNo, t.PackOffset, t.PackLength))
                .ToList();

            if (live.Count > 0)
            {
                ctx.Progress.ResurrectedObjects += live.Count;

                // The object is back in use; its tombstone is stale and would otherwise keep
                // pointing the next run at bytes it must not touch.
                var revived = live.ToList();
                await ctx.Db.ChunkStoreGcTombstones
                    .Where(t => t.ChunkStoreId == ctx.ChunkStoreId
                                && t.Category == bucketKey.Category
                                && revived.Contains(t.Checksum))
                    .ExecuteDeleteAsync(ct);
            }

            if (doomed.Count == 0)
                continue;

            var result = await ctx.Collector.ReclaimAsync(bucket, doomed, ctx.Options, ct);

            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("GC job {JobId}: {Warning}", ctx.Job.Id, warning);
                warnings.Add(warning);
            }

            if (result.ReclaimedHashes.Count > 0)
            {
                ctx.Progress.ReclaimedObjects += result.ReclaimedHashes.Count;
                ctx.Progress.ReclaimedBytes += result.ReclaimedBytes;
                ctx.Progress.PacksCompacted += result.PacksCompacted + result.PacksRetired;

                // Only tombstones whose bytes are actually gone are cleared. Objects the
                // collector deferred — typically because they sit in the pack the bucket is
                // still appending to — keep theirs and are picked up by a later run.
                foreach (var batch in result.ReclaimedHashes.Chunk(DbBatchSize))
                {
                    var gone = batch.ToList();
                    await ctx.Db.ChunkStoreGcTombstones
                        .Where(t => t.ChunkStoreId == ctx.ChunkStoreId
                                    && t.Category == bucketKey.Category
                                    && gone.Contains(t.Checksum))
                        .ExecuteDeleteAsync(ct);
                }
            }

            ctx.Db.ChangeTracker.Clear();
            await PersistProgressAsync(ctx, ct);
        }

        // Superseded pack files become deletable once no reader can still be holding an address
        // into them. This also cleans up after any run that died mid-compaction.
        var purge = await ctx.Collector.PurgeObsoletePacksAsync(ctx.Options.ObsoletePackDrainWindow, ct);
        ctx.Progress.PacksDeleted += purge.PacksDeleted;
        ctx.Progress.PackBytesDeleted += purge.BytesFreed;

        // A completed run that could not rewrite some packs is still a success — but silently
        // succeeding while freeing nothing is exactly how a broken store stays broken.
        if (warnings.Count > 0)
        {
            ctx.Job.ErrorDetails = JsonSerializer.Serialize(
                warnings.Distinct(StringComparer.Ordinal).Take(MaxReportedWarnings)
                    .Select(static w => new { Warning = w }).ToArray());
        }

        await PersistProgressAsync(ctx, ct);
    }

    /// <summary>
    /// The instant before which a quarantine must have started for its objects to be safe to
    /// destroy.
    ///
    /// <para>
    /// An ingest that was told "you already have this object" will not upload it, and relies on
    /// finalize-time resurrection to repair the reference. That only works while the bytes are
    /// still there. So for any ingest session still running, nothing quarantined <em>after</em>
    /// that session began may be destroyed — the session could have asked about it before the
    /// quarantine hid it.
    /// </para>
    /// <para>
    /// In the normal case every session is far younger than the retention window and the barrier
    /// is inert. It earns its place on the pathological one: a session that has been streaming
    /// for longer than the retention window, where the window alone would let the collector
    /// delete data the session still believes it has.
    /// </para>
    /// </summary>
    private static async Task<DateTimeOffset> ComputeReclaimBarrierAsync(GcRunContext ctx, DateTimeOffset now, CancellationToken ct)
    {
        var oldestActiveSession = await ctx.Db.IngestSessions
            .Where(s => s.Repository.ChunkStoreId == ctx.ChunkStoreId
                        && (s.State == IngestSessionState.Created || s.State == IngestSessionState.InProgress)
                        && s.ExpiresAt > now)
            .OrderBy(s => s.StartedAt)
            .Select(s => (DateTimeOffset?)s.StartedAt)
            .FirstOrDefaultAsync(ct);

        return oldestActiveSession is { } started && started < now ? started : now;
    }

    // =======================================================================
    // Progress plumbing

    private async Task PersistProgressAsync(GcRunContext ctx, CancellationToken ct)
    {
        // The job row is the cancellation channel as well as the progress channel: an operator
        // cancelling through the API writes the status, and the run notices it here.
        var freshStatus = await ctx.Db.BackgroundJobs
            .Where(j => j.Id == ctx.Job.Id)
            .Select(j => j.Status)
            .FirstOrDefaultAsync(CancellationToken.None);

        if (freshStatus == BackgroundJobStatus.Cancelled)
            throw new OperationCanceledException($"GC job {ctx.Job.Id} was cancelled by an operator.");

        ctx.Job.ProgressData = JsonSerializer.Serialize(ctx.Progress);
        await ctx.Db.SaveChangesAsync(CancellationToken.None);
        await BroadcastAsync(ctx.Job, ctx.JobData, ctx.Progress, ct);
    }

    private async Task FailAsync(
        BinStashDbContext db,
        BackgroundJob job,
        ChunkStoreGcJobData? jobData,
        ChunkStoreGcProgressData progress,
        string error,
        CancellationToken ct)
    {
        job.Status = BackgroundJobStatus.Failed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ProgressData = JsonSerializer.Serialize(progress);
        job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = error } });

        db.ChangeTracker.Clear();
        db.BackgroundJobs.Update(job);
        await db.SaveChangesAsync(ct);
        await BroadcastAsync(job, jobData, progress, ct);
    }

    private async Task BroadcastAsync(
        BackgroundJob job, ChunkStoreGcJobData? jobData, ChunkStoreGcProgressData progress, CancellationToken ct)
    {
        try
        {
            var dto = new BackgroundJobProgressDto
            {
                JobId = job.Id,
                JobType = job.JobType,
                Status = job.Status.ToString(),
                TotalBuckets = progress.TotalBuckets,
                ProcessedBuckets = progress.ProcessedBuckets,
                FailedBuckets = 0,
                TotalReleases = progress.TotalReleases,
                GcPhase = progress.Phase,
                MarkedReleases = progress.MarkedReleases,
                ReachableObjects = progress.ReachableObjects,
                QuarantinedObjects = progress.QuarantinedObjects,
                QuarantinedBytes = progress.QuarantinedBytes,
                ReclaimedObjects = progress.ReclaimedObjects,
                ReclaimedBytes = progress.ReclaimedBytes,
                PacksCompacted = progress.PacksCompacted,
                PacksDeleted = progress.PacksDeleted,
                PackBytesDeleted = progress.PackBytesDeleted,
                ResurrectedObjects = progress.ResurrectedObjects,
                GcDryRun = jobData?.DryRun ?? false,
                ChunkStoreId = jobData?.ChunkStoreId,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            };

            await _eventSender.SendAsync($"BackgroundJobProgress_{job.Id}", dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast progress for GC job {JobId}", job.Id);
        }
    }
}
