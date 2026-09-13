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
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.ChunkStores;

/// <inheritdoc cref="IGcQuarantineService"/>
public sealed class GcQuarantineService : IGcQuarantineService
{
    /// <summary>
    /// How many hashes go into one <c>IN (...)</c> predicate. Releases routinely reference
    /// hundreds of thousands of chunks, and PostgreSQL's parameter limit is 65535.
    /// </summary>
    private const int LookupBatchSize = 2000;

    private readonly BinStashDbContext _db;
    private readonly ILogger<GcQuarantineService> _logger;

    public GcQuarantineService(BinStashDbContext db, ILogger<GcQuarantineService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> ResurrectAsync(
        Guid chunkStoreId,
        GcObjectCategory category,
        IReadOnlyCollection<Hash32> hashes,
        CancellationToken ct = default)
    {
        if (hashes.Count == 0)
            return 0;

        var resurrected = 0;

        foreach (var batch in hashes.Distinct().Chunk(LookupBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var tombstones = await _db.ChunkStoreGcTombstones
                .Where(t => t.ChunkStoreId == chunkStoreId && t.Category == category && batch.Contains(t.Checksum))
                .ToListAsync(ct);

            if (tombstones.Count == 0)
                continue;

            var quarantinedHashes = tombstones.Select(static t => t.Checksum).ToList();

            // A re-upload during the quarantine window may already have re-created the row. Only
            // insert what is genuinely absent, or the save fails on the primary key.
            var existing = category == GcObjectCategory.Chunk
                ? await _db.Chunks
                    .Where(c => c.ChunkStoreId == chunkStoreId && quarantinedHashes.Contains(c.Checksum))
                    .Select(c => c.Checksum)
                    .ToHashSetAsync(ct)
                : await _db.FileDefinitions
                    .Where(f => f.ChunkStoreId == chunkStoreId && quarantinedHashes.Contains(f.Checksum))
                    .Select(f => f.Checksum)
                    .ToHashSetAsync(ct);

            foreach (var tombstone in tombstones)
            {
                // A zero logical length marks an orphan: bytes with no catalogue row when they
                // were quarantined. There is nothing to restore, and inventing a row with a made
                // up length would be worse than leaving the re-upload path to insert the real one.
                if (!existing.Contains(tombstone.Checksum) && tombstone.LogicalLength > 0)
                {
                    if (category == GcObjectCategory.Chunk)
                    {
                        _db.Chunks.Add(new Chunk
                        {
                            Checksum = tombstone.Checksum,
                            ChunkStoreId = chunkStoreId,
                            Length = (int)tombstone.LogicalLength,
                            CompressedLength = tombstone.CompressedLength
                        });
                    }
                    else
                    {
                        _db.FileDefinitions.Add(new FileDefinition
                        {
                            Checksum = tombstone.Checksum,
                            ChunkStoreId = chunkStoreId,
                            Length = tombstone.LogicalLength
                        });
                    }
                }

                _db.ChunkStoreGcTombstones.Remove(tombstone);
                resurrected++;
            }
        }

        if (resurrected > 0)
        {
            // Worth a log line rather than a silent repair: a steady stream of these means the
            // collector and the ingest workload are fighting over the same objects, and the
            // retention window is too short for how long ingests take.
            _logger.LogInformation(
                "Lifted garbage-collection quarantine from {Count} {Category} object(s) in chunk store {ChunkStoreId}",
                resurrected, category, chunkStoreId);
        }

        return resurrected;
    }

    public async Task<HashSet<Hash32>> GetQuarantinedAsync(
        Guid chunkStoreId,
        GcObjectCategory category,
        IReadOnlyCollection<Hash32> hashes,
        CancellationToken ct = default)
    {
        var result = new HashSet<Hash32>();
        if (hashes.Count == 0)
            return result;

        foreach (var batch in hashes.Distinct().Chunk(LookupBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var found = await _db.ChunkStoreGcTombstones
                .Where(t => t.ChunkStoreId == chunkStoreId && t.Category == category && batch.Contains(t.Checksum))
                .Select(t => t.Checksum)
                .ToListAsync(ct);

            foreach (var hash in found)
                result.Add(hash);
        }

        return result;
    }
}
