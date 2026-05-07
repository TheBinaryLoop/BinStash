// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
using BinStash.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Services.ChunkStores;

public sealed class ChunkStoreStatsCollector
{
    private readonly BinStashDbContext _db;
    private readonly IChunkStoreService _chunkStoreService;

    public ChunkStoreStatsCollector(BinStashDbContext db, IChunkStoreService chunkStoreService)
    {
        _db = db;
        _chunkStoreService = chunkStoreService;
    }

    public async Task<ChunkStoreStatsSnapshot> CollectAndStoreAsync(Guid chunkStoreId, CancellationToken cancellationToken = default)
    {
        var store = await _db.ChunkStores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == chunkStoreId, cancellationToken);

        if (store == null)
            throw new InvalidOperationException($"Chunk store {chunkStoreId} not found.");

        var result = await CollectAsync(store, cancellationToken);

        var snapshot = new ChunkStoreStatsSnapshot
        {
            Id = Guid.CreateVersion7(),
            ChunkStoreId = store.Id,
            CollectedAt = DateTimeOffset.UtcNow,

            ChunkCount = result.ChunkCount,
            FileDefinitionCount = result.FileDefinitionCount,
            ReleaseCount = result.ReleaseCount,

            ChunkPackBytes = result.ChunkPackBytes,
            FileDefinitionPackBytes = result.FileDefinitionPackBytes,
            ReleasePackageBytes = result.ReleasePackageBytes,
            IndexBytes = result.IndexBytes,
            PhysicalBytesTotal = result.PhysicalBytesTotal,

            TotalLogicalBytes = result.TotalLogicalBytes,
            UniqueFileBytes = result.UniqueFileBytes,
            UniqueLogicalChunkBytes = result.UniqueLogicalChunkBytes,
            UniqueCompressedChunkBytes = result.UniqueCompressedChunkBytes,

            ReferencedUniqueChunkBytes = result.ReferencedUniqueChunkBytes,

            CompressionRatio = result.CompressionRatio,
            DeduplicationRatio = result.DeduplicationRatio,
            EffectiveStorageRatio = result.EffectiveStorageRatio,

            CompressionSavedBytes = result.CompressionSavedBytes,
            DeduplicationSavedBytes = result.DeduplicationSavedBytes,

            ChunkPackFileCount = result.ChunkPackFileCount,
            FileDefinitionPackFileCount = result.FileDefinitionPackFileCount,
            ReleasePackageFileCount = result.ReleasePackageFileCount,
            IndexFileCount = result.IndexFileCount,

            VolumeTotalBytes = result.VolumeTotalBytes,
            VolumeFreeBytes = result.VolumeFreeBytes,

            AvgChunkSize = result.AvgChunkSize,
            AvgCompressedChunkSize = result.AvgCompressedChunkSize
        };

        _db.ChunkStoreStatsSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);

        return snapshot;
    }

    public async Task<ChunkStoreStatsResult> CollectAsync(ChunkStore store, CancellationToken cancellationToken = default)
    {
        var chunkStoreId = store.Id;

        var chunkDbStats = await _db.Chunks
            .AsNoTracking()
            .Where(x => x.ChunkStoreId == chunkStoreId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = (long)g.LongCount(),
                LogicalBytes = (long?)g.Sum(x => (long)x.Length) ?? 0,
                CompressedBytes = (long?)g.Sum(x => (long)x.CompressedLength) ?? 0,
                AvgLength = g.Any() ? (long)g.Average(x => x.Length) : 0,
                AvgCompressedLength = g.Any() ? (long)g.Average(x => x.CompressedLength) : 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        var fileDefinitionCount = await _db.FileDefinitions
            .AsNoTracking()
            .LongCountAsync(x => x.ChunkStoreId == chunkStoreId, cancellationToken);

        var releaseCount = await _db.Repositories
            .AsNoTracking()
            .Where(r => r.ChunkStoreId == chunkStoreId)
            .SelectMany(r => r.Releases)
            .LongCountAsync(cancellationToken);

        var physicalStats = await _chunkStoreService.GetPhysicalStatsAsync(store);

        var logicalStats = await CollectLogicalStatsAsync(store, cancellationToken);

        var uniqueLogicalChunkBytes = chunkDbStats?.LogicalBytes ?? 0;
        var uniqueCompressedChunkBytes = chunkDbStats?.CompressedBytes ?? 0;

        var totalLogicalBytes = logicalStats.TotalLogicalBytes;
        var uniqueFileBytes = logicalStats.UniqueFileBytes;
        var referencedUniqueChunkBytes = logicalStats.ReferencedUniqueChunkBytes;

        var compressionRatio = uniqueCompressedChunkBytes > 0
            ? (double)uniqueLogicalChunkBytes / uniqueCompressedChunkBytes
            : 1.0;

        var deduplicationRatio = uniqueLogicalChunkBytes > 0
            ? (double)totalLogicalBytes / uniqueLogicalChunkBytes
            : 1.0;

        var effectiveStorageRatio = physicalStats.PhysicalBytesTotal > 0
            ? (double)totalLogicalBytes / physicalStats.PhysicalBytesTotal
            : 1.0;

        return new ChunkStoreStatsResult
        {
            ChunkCount = chunkDbStats?.Count ?? 0,
            FileDefinitionCount = fileDefinitionCount,
            ReleaseCount = releaseCount,

            ChunkPackBytes = physicalStats.ChunkPackBytes,
            FileDefinitionPackBytes = physicalStats.FileDefinitionPackBytes,
            ReleasePackageBytes = physicalStats.ReleasePackageBytes,
            IndexBytes = physicalStats.IndexBytes,
            PhysicalBytesTotal = physicalStats.PhysicalBytesTotal,

            TotalLogicalBytes = totalLogicalBytes,
            UniqueFileBytes = uniqueFileBytes,
            UniqueLogicalChunkBytes = uniqueLogicalChunkBytes,
            UniqueCompressedChunkBytes = uniqueCompressedChunkBytes,
            ReferencedUniqueChunkBytes = referencedUniqueChunkBytes,

            CompressionRatio = compressionRatio,
            DeduplicationRatio = deduplicationRatio,
            EffectiveStorageRatio = effectiveStorageRatio,

            CompressionSavedBytes = Math.Max(0, uniqueLogicalChunkBytes - uniqueCompressedChunkBytes),
            DeduplicationSavedBytes = Math.Max(0, totalLogicalBytes - uniqueLogicalChunkBytes),

            ChunkPackFileCount = physicalStats.ChunkPackFileCount,
            FileDefinitionPackFileCount = physicalStats.FileDefinitionPackFileCount,
            ReleasePackageFileCount = physicalStats.ReleasePackageFileCount,
            IndexFileCount = physicalStats.IndexFileCount,

            VolumeTotalBytes = physicalStats.VolumeTotalBytes,
            VolumeFreeBytes = physicalStats.VolumeFreeBytes,

            AvgChunkSize = chunkDbStats?.AvgLength ?? 0,
            AvgCompressedChunkSize = chunkDbStats?.AvgCompressedLength ?? 0
        };
    }

    private async Task<LogicalStats> CollectLogicalStatsAsync(ChunkStore store, CancellationToken cancellationToken)
    {
        var chunkStoreId = store.Id;

        var releases = await _db.Releases
            .AsNoTracking()
            .Join(
                _db.Repositories.AsNoTracking(),
                release => release.RepoId,
                repo => repo.Id,
                (release, repo) => new { Release = release, Repo = repo })
            .Where(x => x.Repo.ChunkStoreId == chunkStoreId)
            .Select(x => x.Release)
            .ToListAsync(cancellationToken);

        if (releases.Count == 0)
        {
            return new LogicalStats
            {
                TotalLogicalBytes = 0,
                UniqueFileBytes = 0,
                ReferencedUniqueChunkBytes = 0
            };
        }

        var packageIds = releases
            .Select(r => r.ReleaseDefinitionChecksum.ToHexString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var packageMap = await _chunkStoreService.RetrieveReleasePackagesAsync(store, packageIds);

        long totalLogicalBytes = 0;

        // Unique stored content hashes referenced by all output artifacts across all releases
        var uniqueContentHashes = new HashSet<Hash32>();

        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageId = release.ReleaseDefinitionChecksum.ToHexString();
            if (!packageMap.TryGetValue(packageId, out var packageData))
                continue;

            var package = await ReleasePackageSerializer.DeserializeAsync(packageData, cancellationToken);
            var outputArtifacts = package.OutputArtifacts ?? [];

            if (outputArtifacts.Count == 0)
                continue;

            var artifactContentHashes = CollectReferencedContentHashes(outputArtifacts);

            Dictionary<Hash32, long> fileLengthsInRelease = [];
            if (artifactContentHashes.Count > 0)
            {
                fileLengthsInRelease = await _db.FileDefinitions
                    .AsNoTracking()
                    .Where(x => x.ChunkStoreId == chunkStoreId && artifactContentHashes.Contains(x.Checksum))
                    .ToDictionaryAsync(x => x.Checksum, x => x.Length, cancellationToken);
            }

            foreach (var artifact in outputArtifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                totalLogicalBytes += CalculateLogicalArtifactSize(artifact, fileLengthsInRelease);

                foreach (var hash in CollectReferencedContentHashes([artifact]))
                    uniqueContentHashes.Add(hash);
            }
        }

        long uniqueFileBytes = 0;
        if (uniqueContentHashes.Count > 0)
        {
            uniqueFileBytes = await _db.FileDefinitions
                .AsNoTracking()
                .Where(x => x.ChunkStoreId == chunkStoreId && uniqueContentHashes.Contains(x.Checksum))
                .SumAsync(x => (long?)x.Length, cancellationToken) ?? 0;
        }

        var fileHashStrings = uniqueContentHashes
            .Select(x => x.ToHexString())
            .ToArray();

        var fileDefinitionMap = fileHashStrings.Length > 0
            ? await _chunkStoreService.RetrieveFileDefinitionsAsync(store, fileHashStrings)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var referencedUniqueChunks = new HashSet<Hash32>();

        foreach (var fileDefinitionData in fileDefinitionMap.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var chunkHash in ChecksumCompressor.TransposeDecompressHashes(fileDefinitionData))
                referencedUniqueChunks.Add(chunkHash);
        }

        long referencedUniqueChunkBytes = 0;
        if (referencedUniqueChunks.Count > 0)
        {
            referencedUniqueChunkBytes = await _db.Chunks
                .AsNoTracking()
                .Where(x => x.ChunkStoreId == chunkStoreId && referencedUniqueChunks.Contains(x.Checksum))
                .SumAsync(x => (long?)x.Length, cancellationToken) ?? 0;
        }

        return new LogicalStats
        {
            TotalLogicalBytes = totalLogicalBytes,
            UniqueFileBytes = uniqueFileBytes,
            ReferencedUniqueChunkBytes = referencedUniqueChunkBytes
        };
    }

    private static List<Hash32> CollectReferencedContentHashes(IEnumerable<OutputArtifact> outputArtifacts)
    {
        var hashes = new List<Hash32>();

        foreach (var artifact in outputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    if (opaque.ContentHash != null)
                        hashes.Add(opaque.ContentHash.Value);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash != null)
                            hashes.Add(member.ContentHash.Value);
                    }
                    break;
            }
        }

        return hashes;
    }

    private static long CalculateLogicalArtifactSize(OutputArtifact artifact, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        return artifact.Backing switch
        {
            OpaqueBlobBacking opaque => CalculateOpaqueArtifactSize(opaque, fileLengths),
            ReconstructedContainerBacking reconstructed => CalculateReconstructedArtifactSize(reconstructed, fileLengths),
            _ => 0L
        };
    }

    private static long CalculateOpaqueArtifactSize(OpaqueBlobBacking backing, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        if (backing.Length.HasValue)
            return backing.Length.Value;

        if (backing.ContentHash != null && fileLengths.TryGetValue(backing.ContentHash.Value, out var len))
            return len;

        return 0L;
    }

    private static long CalculateReconstructedArtifactSize(ReconstructedContainerBacking backing, IReadOnlyDictionary<Hash32, long> fileLengths)
    {
        long total = 0;

        foreach (var member in backing.Members)
        {
            if (member.Length.HasValue)
            {
                total += member.Length.Value;
                continue;
            }

            if (member.ContentHash != null && fileLengths.TryGetValue(member.ContentHash.Value, out var len))
                total += len;
        }

        return total;
    }
    
    public sealed class ChunkStoreStatsResult
    {
        public long ChunkCount { get; set; }
        public long FileDefinitionCount { get; set; }
        public long ReleaseCount { get; set; }

        public long ChunkPackBytes { get; set; }
        public long FileDefinitionPackBytes { get; set; }
        public long ReleasePackageBytes { get; set; }
        public long IndexBytes { get; set; }
        public long PhysicalBytesTotal { get; set; }

        public long TotalLogicalBytes { get; set; }
        public long UniqueFileBytes { get; set; }
        public long UniqueLogicalChunkBytes { get; set; }
        public long UniqueCompressedChunkBytes { get; set; }
        public long ReferencedUniqueChunkBytes { get; set; }

        public double CompressionRatio { get; set; }
        public double DeduplicationRatio { get; set; }
        public double EffectiveStorageRatio { get; set; }

        public long CompressionSavedBytes { get; set; }
        public long DeduplicationSavedBytes { get; set; }

        public int ChunkPackFileCount { get; set; }
        public int FileDefinitionPackFileCount { get; set; }
        public int ReleasePackageFileCount { get; set; }
        public int IndexFileCount { get; set; }

        public long VolumeTotalBytes { get; set; }
        public long VolumeFreeBytes { get; set; }

        public long AvgChunkSize { get; set; }
        public long AvgCompressedChunkSize { get; set; }
    }

    private sealed class LogicalStats
    {
        public long TotalLogicalBytes { get; set; }
        public long UniqueFileBytes { get; set; }
        public long ReferencedUniqueChunkBytes { get; set; }
    }
}