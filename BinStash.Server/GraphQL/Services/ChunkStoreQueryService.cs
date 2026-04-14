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

namespace BinStash.Server.GraphQL.Services;

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

    private static ChunkStoreGql MapToGql(ChunkStore store) => new()
    {
        Id = store.Id,
        Name = store.Name,
        Type = store.Type.ToString(),
        BackendSettings = MapBackendSettingsToGql(store.BackendSettings)
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