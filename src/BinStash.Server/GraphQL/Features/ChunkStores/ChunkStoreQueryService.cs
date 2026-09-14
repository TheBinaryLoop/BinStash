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

using BinStash.Core.Auth.Instance;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.ChunkStores;

public sealed class ChunkStoreQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public ChunkStoreQueryService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }
    
    public async Task<ChunkStoreGql?> GetChunkStoreByIdAsync(Guid chunkStoreId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
        
        var store = await _db.ChunkStores
            .AsNoTracking()
            .FirstOrDefaultAsync(cs => cs.Id == chunkStoreId, ct);

        return store is null ? null : MapToGql(store);
    }
    
    public async Task<IQueryable<ChunkStoreGql>> GetChunkStoresAsync(CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
        
        // Load all stores in-memory because BackendSettings is a JSON-converted column
        // that cannot be projected via EF Core LINQ-to-SQL.
        var stores = await _db.ChunkStores
            .AsNoTracking()
            .ToListAsync(ct);

        return stores.Select(MapToGql).AsQueryable();
    }

    public async Task<ChunkStoreStatsGql?> GetChunkStoreStatsAsync(Guid chunkStoreId, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        var exists = await _db.ChunkStores.AnyAsync(cs => cs.Id == chunkStoreId, ct);
        if (!exists)
            return null;

        var snapshot = await _db.ChunkStoreStatsSnapshots
            .AsNoTracking()
            .Where(s => s.ChunkStoreId == chunkStoreId)
            .OrderByDescending(s => s.CollectedAt)
            .FirstOrDefaultAsync(ct);

        // Chunk count is the one figure worth paying for live: it is a single indexed count, and
        // it is the number that moves between hourly snapshots on an actively-ingested store.
        var totalChunks = await _db.Chunks.CountAsync(x => x.ChunkStoreId == chunkStoreId, ct);

        return MapStatsToGql(totalChunks, snapshot);
    }

    /// <summary>
    /// Snapshots over a trailing window, oldest first.
    /// </summary>
    /// <remarks>
    /// The collector has been writing a row an hour all along, so growth over time is already in
    /// the database — it just had no way out of it. A series answers questions a single reading
    /// cannot: whether a store's free space is trending toward zero, and whether a collection run
    /// actually returned anything.
    /// </remarks>
    public async Task<List<ChunkStoreStatsGql>> GetChunkStoreStatsHistoryAsync(Guid chunkStoreId, int days, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        if (days is < 1 or > 365)
            throw new GraphQLException("History window must be between 1 and 365 days.");

        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var snapshots = await _db.ChunkStoreStatsSnapshots
            .AsNoTracking()
            .Where(s => s.ChunkStoreId == chunkStoreId && s.CollectedAt >= since)
            .OrderBy(s => s.CollectedAt)
            .ToListAsync(ct);

        // TotalChunks mirrors the snapshot's own count here rather than the live one: every point
        // in a series has to be as-of its own timestamp, or the last point would silently differ
        // in kind from the rest.
        return snapshots.Select(s => MapStatsToGql((int)Math.Min(s.ChunkCount, int.MaxValue), s)).ToList();
    }

    private static ChunkStoreStatsGql MapStatsToGql(int totalChunks, ChunkStoreStatsSnapshot? s) => new()
    {
        TotalChunks = totalChunks,
        CollectedAt = s?.CollectedAt,
        ChunkCount = s?.ChunkCount ?? 0,
        FileDefinitionCount = s?.FileDefinitionCount ?? 0,
        ReleaseCount = s?.ReleaseCount ?? 0,
        ChunkPackBytes = s?.ChunkPackBytes ?? 0,
        FileDefinitionPackBytes = s?.FileDefinitionPackBytes ?? 0,
        ReleasePackageBytes = s?.ReleasePackageBytes ?? 0,
        IndexBytes = s?.IndexBytes ?? 0,
        PhysicalBytesTotal = s?.PhysicalBytesTotal ?? 0,
        TotalLogicalBytes = s?.TotalLogicalBytes ?? 0,
        UniqueFileBytes = s?.UniqueFileBytes ?? 0,
        UniqueLogicalChunkBytes = s?.UniqueLogicalChunkBytes ?? 0,
        UniqueCompressedChunkBytes = s?.UniqueCompressedChunkBytes ?? 0,
        ReferencedUniqueChunkBytes = s?.ReferencedUniqueChunkBytes ?? 0,
        CompressionRatio = s?.CompressionRatio ?? 0,
        DeduplicationRatio = s?.DeduplicationRatio ?? 0,
        EffectiveStorageRatio = s?.EffectiveStorageRatio ?? 0,
        CompressionSavedBytes = s?.CompressionSavedBytes ?? 0,
        DeduplicationSavedBytes = s?.DeduplicationSavedBytes ?? 0,
        ChunkPackFileCount = s?.ChunkPackFileCount ?? 0,
        FileDefinitionPackFileCount = s?.FileDefinitionPackFileCount ?? 0,
        ReleasePackageFileCount = s?.ReleasePackageFileCount ?? 0,
        IndexFileCount = s?.IndexFileCount ?? 0,
        VolumeTotalBytes = s?.VolumeTotalBytes ?? 0,
        VolumeFreeBytes = s?.VolumeFreeBytes ?? 0,
        AvgChunkSize = s?.AvgChunkSize ?? 0,
        AvgCompressedChunkSize = s?.AvgCompressedChunkSize ?? 0
    };

    public async Task<List<ChunkStoreTypeInfoGql>> GetEnabledChunkStoreTypesAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        return Enum.GetValues<BinStash.Core.Entities.ChunkStoreType>()
            .Select(x => new ChunkStoreTypeInfoGql { Name = x.ToString(), Value = (int)x })
            .ToList();
    }

    private static ChunkStoreGql MapToGql(ChunkStore store) => new()
    {
        Id = store.Id,
        Name = store.Name,
        Type = store.Type.ToString(),
        Chunker = new ChunkStoreChunkerGql
        {
            Type = store.ChunkerOptions.Type.ToString(),
            MinChunkSize = store.ChunkerOptions.MinChunkSize,
            AvgChunkSize = store.ChunkerOptions.AvgChunkSize,
            MaxChunkSize = store.ChunkerOptions.MaxChunkSize
        },
        BackendSettings = MapBackendSettingsToGql(store.BackendSettings),
        ProbeMode = store.ProbeMode.ToString(),
        MinFreeBytes = store.MinFreeBytes
    };

    private static ChunkStoreBackendSettingsGql? MapBackendSettingsToGql(ChunkStoreBackendSettings? settings) => settings switch
    {
        LocalFolderBackendSettings local => new ChunkStoreBackendSettingsGql
        {
            BackendType = "LocalFolder",
            LocalPath = local.Path
        },
        _ => null
    };
}