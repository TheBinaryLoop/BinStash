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

using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.ChunkStores;

/// <summary>
/// Shared lookups over the <see cref="BackgroundJob"/> rows that belong to one chunk store.
///
/// <para>
/// Which store a job targets lives inside its JSON <see cref="BackgroundJob.JobData"/> rather
/// than in a column, so every caller has to match on the serialized payload. Doing that in one
/// place keeps the operator-triggered path and the scheduler from drifting apart on what counts
/// as "already busy" — a disagreement there would let two collections, or a collection and a
/// rebuild, run over the same store.
/// </para>
/// </summary>
public static class ChunkStoreJobQueries
{
    /// <summary>
    /// Jobs of <paramref name="jobType"/> that target <paramref name="chunkStoreId"/>.
    /// </summary>
    /// <remarks>
    /// Matched with jsonb containment (<c>@&gt;</c>) rather than a substring test. The column is
    /// <c>jsonb</c>, not text, so <c>string.Contains</c> has no translation at all — it either
    /// fails outright or falls back to loading every job row and filtering in memory.
    /// Containment also asks the precise question: that the payload has this <c>ChunkStoreId</c>,
    /// not merely that the id appears somewhere in it.
    /// </remarks>
    public static IQueryable<BackgroundJob> ForChunkStore(
        this IQueryable<BackgroundJob> jobs, string jobType, Guid chunkStoreId)
    {
        var pattern = ChunkStoreIdPattern(chunkStoreId);

        return jobs.Where(j => j.JobType == jobType
                               && j.JobData != null
                               && EF.Functions.JsonContains(j.JobData, pattern));
    }

    /// <summary>
    /// The minimal jsonb document a job payload must contain to belong to this store.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than serialized from a job-data type: it has to mirror how those
    /// payloads were written, and every one of them uses <see cref="System.Text.Json"/>'s
    /// defaults — PascalCase names, and a GUID as a lowercase hyphenated string.
    /// </remarks>
    private static string ChunkStoreIdPattern(Guid chunkStoreId)
        => $$"""{"ChunkStoreId":"{{chunkStoreId}}"}""";

    /// <summary>
    /// True when a collection or a rebuild is already queued or running for this store.
    ///
    /// <para>
    /// The two are mutually exclusive, not just self-exclusive: a rebuild tears down the bucket
    /// indexes and recreates them, and a collection reads those same indexes to decide what to
    /// destroy. Overlapping them would mean deciding against an index that is being replaced.
    /// </para>
    /// </summary>
    public static async Task<bool> HasActiveMaintenanceJobAsync(
        BinStashDbContext db, Guid chunkStoreId, CancellationToken ct)
    {
        var pattern = ChunkStoreIdPattern(chunkStoreId);

        return await db.BackgroundJobs
            .AsNoTracking()
            .AnyAsync(j => (j.JobType == BackgroundJobTypes.ChunkStoreGc || j.JobType == BackgroundJobTypes.ChunkStoreRebuild)
                           && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running)
                           && j.JobData != null
                           && EF.Functions.JsonContains(j.JobData, pattern), ct);
    }

    /// <summary>
    /// When collection last <em>started</em> on this store, or null if it never has.
    ///
    /// <para>
    /// Start rather than completion, and any outcome rather than only successes. A run that
    /// failed still consumed the I/O and still points at whatever made it fail; re-queuing it
    /// every tick would turn one broken store into a hot loop.
    /// </para>
    /// </summary>
    public static async Task<DateTimeOffset?> LastGcRunStartedAtAsync(
        BinStashDbContext db, Guid chunkStoreId, CancellationToken ct)
    {
        var last = await db.BackgroundJobs
            .AsNoTracking()
            .ForChunkStore(BackgroundJobTypes.ChunkStoreGc, chunkStoreId)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new { j.StartedAt, j.CreatedAt })
            .FirstOrDefaultAsync(ct);

        return last is null ? null : last.StartedAt ?? last.CreatedAt;
    }
}
