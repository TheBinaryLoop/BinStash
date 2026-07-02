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

namespace BinStash.Server.GraphQL.Features.StorageClasses;

public sealed class StorageClassMutationService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public StorageClassMutationService(
        BinStashDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Replaces the full set of instance-wide default storage-class mappings with the supplied set.
    /// </summary>
    public async Task<bool> SetStorageClassDefaultMappingsAsync(SetStorageClassDefaultMappingsInput input, CancellationToken cancellationToken)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        var validChunkStoreIds = await _db.ChunkStores.Select(c => c.Id).ToListAsync(cancellationToken);
        var validStorageClassNames = await _db.StorageClasses.Select(s => s.Name).ToListAsync(cancellationToken);

        foreach (var mapping in input.Mappings)
        {
            if (!validStorageClassNames.Contains(mapping.StorageClassName))
                throw new GraphQLException($"Unknown storage class '{mapping.StorageClassName}'.");
            if (!validChunkStoreIds.Contains(mapping.ChunkStoreId))
                throw new GraphQLException($"Unknown chunk store '{mapping.ChunkStoreId}'.");
        }

        var existing = await _db.StorageClassDefaultMappings.ToListAsync(cancellationToken);
        _db.StorageClassDefaultMappings.RemoveRange(existing);

        foreach (var mapping in input.Mappings)
        {
            _db.StorageClassDefaultMappings.Add(new StorageClassDefaultMapping
            {
                StorageClassName = mapping.StorageClassName,
                ChunkStoreId = mapping.ChunkStoreId,
                IsDefault = mapping.IsDefault,
                IsEnabled = mapping.IsEnabled,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
