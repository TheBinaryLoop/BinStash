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
using BinStash.Infrastructure.Data;
using BinStash.Server.Services.ChunkStores;
using HotChocolate.Subscriptions;
using Microsoft.EntityFrameworkCore;

using BinStash.Server.Services.Releases;

namespace BinStash.Server.Services.ReleaseUpgrade;

/// <summary>
/// Safe release upgrade implementation.
///
/// Algorithm per release:
///   1. Read old .rdef from storage (by current checksum).
///   2. Deserialize with backward-compatible deserializer.
///   3. Re-serialize to latest version.
///   4. Compute BLAKE3 hash of the new bytes.
///   5. Write new .rdef to storage (content-addressed, so a new hash = new file).
///   6. Update DB row (SerializerVersion, ReleaseDefinitionChecksum).
///   7. Only AFTER the DB commit succeeds, delete the OLD .rdef file.
///      If the old hash == new hash (content unchanged), skip deletion entirely.
///   8. If deletion fails, log but do NOT roll back — the data is safe in the new file.
/// </summary>
public sealed class ReleaseUpgradeService : IReleaseUpgradeService
{
    private const int BatchSize = 25;

    private readonly IServiceProvider _services;
    private readonly ITopicEventSender _eventSender;
    private readonly ILogger<ReleaseUpgradeService> _logger;

    public ReleaseUpgradeService(IServiceProvider services, ITopicEventSender eventSender, ILogger<ReleaseUpgradeService> logger)
    {
        _services = services;
        _eventSender = eventSender;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinStashDbContext>();
        var chunkStoreService = scope.ServiceProvider.GetRequiredService<IChunkStoreService>();

        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            _logger.LogError("Upgrade job {JobId} not found", jobId);
            return;
        }

        if (job.Status is BackgroundJobStatus.Cancelled)
        {
            _logger.LogInformation("Upgrade job {JobId} was cancelled before starting", jobId);
            return;
        }

        var jobData = JsonSerializer.Deserialize<ReleaseUpgradeJobData>(job.JobData ?? "{}");
        if (jobData is null)
        {
            _logger.LogError("Upgrade job {JobId} has invalid JobData", jobId);
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = "Invalid JobData payload" } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgress(job, jobData, cancellationToken);
            return;
        }

        var store = await db.ChunkStores.FindAsync([jobData.ChunkStoreId], cancellationToken);
        if (store is null)
        {
            _logger.LogError("Upgrade job {JobId}: ChunkStore {ChunkStoreId} not found", jobId, jobData.ChunkStoreId);
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = $"ChunkStore {jobData.ChunkStoreId} not found" } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgress(job, jobData, cancellationToken);
            return;
        }

        var progress = new ReleaseUpgradeProgressData();

        // Mark running
        job.Status = BackgroundJobStatus.Running;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.ProgressData = JsonSerializer.Serialize(progress);
        await db.SaveChangesAsync(cancellationToken);
        await BroadcastProgress(job, jobData, cancellationToken);

        try
        {
            // Gather releases that need upgrading — only those with SerializerVersion < target
            var repoIds = await db.Repositories
                .Where(r => r.ChunkStoreId == store.Id)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            if (repoIds.Count == 0)
            {
                await CompleteJob(db, job, progress, jobData, cancellationToken);
                return;
            }

            // Releases needing a format upgrade, plus releases already at the target version
            // whose content metrics were never recorded (builds before 2026-03 did not write
            // logical size or chunk count). Both cases need the package read; only the first
            // needs it rewritten.
            var variantsMissingMetrics = db.ReleaseMetrics
                .Where(m => m.TotalLogicalBytes == 0 || m.ChunksInRelease == 0)
                .Select(m => m.VariantId);

            // Upgrading is per variant, because the definition being rewritten is per variant.
            // A release whose Linux target is current and whose Windows target is not upgrades
            // only the one that needs it.
            var releasesToUpgrade = await db.ReleaseVariants
                .Where(v => repoIds.Contains(v.Release.RepoId)
                            && (v.SerializerVersion < jobData.TargetSerializerVersion
                                || variantsMissingMetrics.Contains(v.Id)))
                .ToListAsync(cancellationToken);

            progress.TotalReleases = releasesToUpgrade.Count;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(cancellationToken);
            await BroadcastProgress(job, jobData, cancellationToken);

            if (releasesToUpgrade.Count == 0)
            {
                await CompleteJob(db, job, progress, jobData, cancellationToken);
                return;
            }

            var errorList = new List<object>();
            // Track old checksums for deferred deletion (delete only after batch commit)
            var pendingDeletions = new List<(string OldHash, string NewHash)>();

            for (var i = 0; i < releasesToUpgrade.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Re-check cancellation from DB (user may have canceled via API)
                if ((i % BatchSize) == 0 && i > 0)
                {
                    var freshStatus = await db.BackgroundJobs
                        .Where(j => j.Id == jobId)
                        .Select(j => j.Status)
                        .FirstAsync(cancellationToken);

                    if (freshStatus == BackgroundJobStatus.Cancelled)
                    {
                        _logger.LogInformation("Upgrade job {JobId} cancelled by user at release {Index}/{Total}", jobId, i, releasesToUpgrade.Count);
                        job.Status = BackgroundJobStatus.Cancelled;
                        job.CompletedAt = DateTimeOffset.UtcNow;
                        job.ErrorDetails = errorList.Count > 0 ? JsonSerializer.Serialize(errorList) : null;
                        job.ProgressData = JsonSerializer.Serialize(progress);
                        await db.SaveChangesAsync(CancellationToken.None);
                        await BroadcastProgress(job, jobData, CancellationToken.None);
                        return;
                    }
                }

                var release = releasesToUpgrade[i];
                var oldHash = release.ReleaseDefinitionChecksum.ToHexString();

                try
                {
                    // Step 1: Read old .rdef
                    var oldData = await chunkStoreService.RetrieveReleasePackageAsync(store, oldHash);
                    if (oldData is null)
                    {
                        progress.FailedReleases++;
                        errorList.Add(new { ReleaseId = release.Id, Error = "Release package not found in storage" });
                        _logger.LogWarning("Upgrade job {JobId}: release {ReleaseId} — .rdef not found at hash {Hash}", jobId, release.Id, oldHash);
                        continue;
                    }

                    // Step 2: Deserialize (backward-compatible)
                    var (package, _) = await ReleasePackageSerializer.DeserializeAsync(oldData, cancellationToken);

                    // Step 2a: Populate null Length fields from FileDefinition table.
                    // V1/V2 formats did not store file lengths; the V6 serializer requires them.
                    // Look up missing lengths via ContentHash → FileDefinition.Length.
                    //
                    // This MUST run before the metrics below: logical size is the sum of those
                    // very Length fields, so recomputing first would score every V1/V2 release
                    // as 0 bytes — precisely the gap the backfill exists to close.
                    await PopulateMissingLengthsAsync(db, package, store.Id, cancellationToken);

                    // Step 2b: Refresh the derived content metrics while the package is open.
                    // Deliberately isolated: a metrics failure must not abort a format upgrade
                    // that has already rewritten stored data.
                    try
                    {
                        await RecalculateMetricsAsync(db, chunkStoreService, store, package, release.Id, cancellationToken);
                    }
                    catch (Exception metricsEx)
                    {
                        _logger.LogWarning(metricsEx,
                            "Upgrade job {JobId}: could not recompute metrics for release {ReleaseId}",
                            jobId, release.Id);
                    }

                    if (release.SerializerVersion >= jobData.TargetSerializerVersion)
                    {
                        // Already on the target format — it was selected only to backfill
                        // metrics, so do not rewrite the .rdef. Fall through to the batch
                        // commit rather than continuing the loop: on a store where every
                        // release takes this path, skipping it would defer all recomputed
                        // metrics to a single commit at the very end and leave the job
                        // reporting no progress throughout.
                        progress.SkippedReleases++;
                    }
                    else
                    {
                        // Step 3: Re-serialize to latest version
                        var (newData, _) = await ReleasePackageSerializer.SerializeAsync(package, cancellationToken: cancellationToken);

                        // Step 4: Compute new BLAKE3 hash
                        var newHash = new Hash32(Blake3.Hasher.Hash(newData).AsSpan());
                        var newHashHex = newHash.ToHexString();

                        // Track size delta
                        var delta = newData.Length - oldData.Length;
                        if (delta < 0)
                            progress.BytesSaved += Math.Abs(delta);
                        else if (delta > 0)
                            progress.BytesGrown += delta;

                        // Step 5: Write new .rdef to storage (idempotent if hash unchanged)
                        if (oldHash != newHashHex)
                        {
                            await chunkStoreService.StoreReleasePackageAsync(store, newData);
                        }

                        // Step 6: Update DB row
                        release.SerializerVersion = jobData.TargetSerializerVersion;
                        release.ReleaseDefinitionChecksum = newHash;

                        // Track for deferred deletion
                        pendingDeletions.Add((oldHash, newHashHex));
                        progress.ProcessedReleases++;
                    }
                }
                catch (Exception ex)
                {
                    progress.FailedReleases++;
                    errorList.Add(new { ReleaseId = release.Id, Error = ex.Message });
                    _logger.LogError(ex, "Upgrade job {JobId}: failed to upgrade release {ReleaseId}", jobId, release.Id);
                }

                // Batch commit + deferred deletion
                var isLastRelease = i == releasesToUpgrade.Count - 1;
                var isBatchBoundary = (i + 1) % BatchSize == 0;

                if (isBatchBoundary || isLastRelease)
                {
                    // Step 6 (cont.): Batch DB commit
                    job.ProgressData = JsonSerializer.Serialize(progress);
                    await db.SaveChangesAsync(cancellationToken);

                    // Step 7: Delete old .rdef files only after successful commit
                    foreach (var (oldH, newH) in pendingDeletions)
                    {
                        if (oldH == newH) continue; // Content unchanged, same file
                        try
                        {
                            await chunkStoreService.DeleteReleasePackageAsync(store, oldH);
                        }
                        catch (Exception ex)
                        {
                            // Non-fatal: the new file is already written and the DB points to it.
                            // The old file becomes an orphan but no data is lost.
                            _logger.LogWarning(ex, "Upgrade job {JobId}: failed to delete old .rdef {Hash} (orphaned, non-fatal)", jobId, oldH);
                        }
                    }

                    pendingDeletions.Clear();
                    await BroadcastProgress(job, jobData, cancellationToken);
                }
            }

            // Finalize
            job.ErrorDetails = errorList.Count > 0 ? JsonSerializer.Serialize(errorList) : null;
            await CompleteJob(db, job, progress, jobData, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Status = BackgroundJobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgress(job, jobData, CancellationToken.None);
            _logger.LogInformation("Upgrade job {JobId} cancelled via token", jobId);
        }
        catch (Exception ex)
        {
            job.Status = BackgroundJobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProgressData = JsonSerializer.Serialize(progress);
            job.ErrorDetails = JsonSerializer.Serialize(new[] { new { Error = ex.ToString() } });
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastProgress(job, jobData, CancellationToken.None);
            _logger.LogError(ex, "Upgrade job {JobId} failed with unhandled exception", jobId);
        }
    }

    private async Task CompleteJob(BinStashDbContext db, BackgroundJob job, ReleaseUpgradeProgressData progress, ReleaseUpgradeJobData? jobData, CancellationToken ct)
    {
        job.Status = progress.FailedReleases > 0
            ? BackgroundJobStatus.Failed
            : BackgroundJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ProgressData = JsonSerializer.Serialize(progress);
        await db.SaveChangesAsync(ct);
        await BroadcastProgress(job, jobData, ct);
    }

    /// <summary>
    /// Populates null <see cref="OpaqueBlobBacking.Length"/> and <see cref="ContainerMemberBinding.Length"/>
    /// fields by looking up the file size from the <see cref="FileDefinition"/> table.
    /// V1/V2 release formats did not store file lengths; the V4 serializer requires them.
    /// The <see cref="FileDefinition.Checksum"/> (BLAKE3 whole-file hash) matches
    /// <see cref="OpaqueBlobBacking.ContentHash"/> and <see cref="ContainerMemberBinding.ContentHash"/>.
    /// </summary>
    /// <summary>
    /// Recomputes and persists the release's logical size and chunk count.
    /// </summary>
    private static async Task RecalculateMetricsAsync(
        BinStashDbContext db,
        IChunkStoreService chunkStoreService,
        ChunkStore store,
        ReleasePackage package,
        Guid variantId,
        CancellationToken ct)
    {
        var metrics = await db.ReleaseMetrics.FirstOrDefaultAsync(m => m.VariantId == variantId, ct);
        if (metrics is null)
            return;

        metrics.TotalLogicalBytes = ReleaseMetricsCalculator.CalculateLogicalBytes(package);
        metrics.ChunksInRelease = await ReleaseMetricsCalculator.CountUniqueChunksAsync(
            package, store, chunkStoreService, ct);
    }

    private static async Task PopulateMissingLengthsAsync(BinStashDbContext db, ReleasePackage package, Guid chunkStoreId, CancellationToken cancellationToken)
    {
        // Collect content hashes that need length lookup
        var hashesNeedingLength = new HashSet<Hash32>();

        foreach (var artifact in package.OutputArtifacts ?? [])
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking { Length: null, ContentHash: not null } opaque:
                    hashesNeedingLength.Add(opaque.ContentHash.Value);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.Length == null && member.ContentHash != null)
                            hashesNeedingLength.Add(member.ContentHash.Value);
                    }
                    break;
            }
        }

        if (hashesNeedingLength.Count == 0)
            return;

        // Batch-query FileDefinition table for the missing lengths
        var fileLengths = await db.FileDefinitions
            .AsNoTracking()
            .Where(fd => fd.ChunkStoreId == chunkStoreId && hashesNeedingLength.Contains(fd.Checksum))
            .ToDictionaryAsync(fd => fd.Checksum, fd => fd.Length, cancellationToken);

        // Apply the looked-up lengths back to the deserialized package
        foreach (var artifact in package.OutputArtifacts ?? [])
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking { Length: null, ContentHash: not null } opaque:
                    if (fileLengths.TryGetValue(opaque.ContentHash.Value, out var opaqueLen))
                        opaque.Length = opaqueLen;
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.Length == null && member.ContentHash != null &&
                            fileLengths.TryGetValue(member.ContentHash.Value, out var memberLen))
                            member.Length = memberLen;
                    }
                    break;
            }
        }
    }

    private async Task BroadcastProgress(BackgroundJob job, ReleaseUpgradeJobData? jobData, CancellationToken ct)
    {
        try
        {
            var progress = !string.IsNullOrEmpty(job.ProgressData)
                ? JsonSerializer.Deserialize<ReleaseUpgradeProgressData>(job.ProgressData)
                : new ReleaseUpgradeProgressData();

            var dto = new BackgroundJobProgressDto
            {
                JobId = job.Id,
                JobType = job.JobType,
                Status = job.Status.ToString(),
                TotalReleases = progress?.TotalReleases ?? 0,
                ProcessedReleases = progress?.ProcessedReleases ?? 0,
                FailedReleases = progress?.FailedReleases ?? 0,
                SkippedReleases = progress?.SkippedReleases ?? 0,
                BytesSaved = progress?.BytesSaved ?? 0,
                BytesGrown = progress?.BytesGrown ?? 0,
                ChunkStoreId = jobData?.ChunkStoreId,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            };

            // Topic: "BackgroundJobProgress_{jobId}" — clients subscribe per job
            await _eventSender.SendAsync($"BackgroundJobProgress_{job.Id}", dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast progress for job {JobId}", job.Id);
        }
    }
}

/// <summary>
/// DTO broadcast via GraphQL subscriptions on each progress update.
/// Fields relevant to upgrade jobs are populated for <c>ReleaseUpgrade</c> job types;
/// fields relevant to rebuild jobs are populated for <c>ChunkStoreRebuild</c> job types;
/// fields relevant to collection are populated for <c>ChunkStoreGc</c> job types.
/// </summary>
/// <remarks>
/// One flat DTO across three unrelated job shapes, which is why every job type must contribute
/// its own progress fields here. A job whose fields are missing does not fail — it streams zeros,
/// and a client showing "0 / 0" cannot tell that apart from a run that has not started.
/// </remarks>
public sealed class BackgroundJobProgressDto
{
    public Guid JobId { get; init; }
    public required string JobType { get; init; }
    public required string Status { get; init; }
    // --- ReleaseUpgrade fields ---
    public int TotalReleases { get; init; }
    public int ProcessedReleases { get; init; }
    public int FailedReleases { get; init; }
    public int SkippedReleases { get; init; }
    public long BytesSaved { get; init; }
    public long BytesGrown { get; init; }
    // --- ChunkStoreRebuild fields ---
    public int TotalBuckets { get; init; }
    public int ProcessedBuckets { get; init; }
    public int FailedBuckets { get; init; }
    // --- ChunkStoreGc fields ---

    /// <summary>
    /// Which phase the run is in. Collection has no single monotonic counter — mark walks
    /// releases, sweep walks buckets — so the phase is what makes a percentage interpretable.
    /// </summary>
    public string? GcPhase { get; init; }

    public int MarkedReleases { get; init; }
    public long ResolvedFileDefinitions { get; init; }
    public int ProcessedFileDefinitionGroups { get; init; }
    public int TotalFileDefinitionGroups { get; init; }
    public long ReachableObjects { get; init; }
    public long QuarantinedObjects { get; init; }
    public long QuarantinedBytes { get; init; }
    public long ReclaimedObjects { get; init; }
    public long ReclaimedBytes { get; init; }
    public long DeferredObjects { get; init; }
    public int PacksSealed { get; init; }
    public int PacksCompacted { get; init; }
    public int PacksDeleted { get; init; }
    public long PackBytesDeleted { get; init; }
    public long ResurrectedObjects { get; init; }

    /// <summary>True when the run reports what it would collect and changes nothing.</summary>
    public bool GcDryRun { get; init; }

    // --- Common ---
    public Guid? ChunkStoreId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
