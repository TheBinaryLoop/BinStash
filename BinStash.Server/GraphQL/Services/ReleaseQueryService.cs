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

using System.Text.Json;
using BinStash.Core.Auth.Repository;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Services;

public sealed class ReleaseQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public ReleaseQueryService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }
    
    public async Task<ReleaseGql?> GetReleaseByIdAsync(Guid releaseId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);

        var releaseMeta = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Id == releaseId && r.Repository.TenantId == tenantContext.TenantId)
            .Select(r => new
            {
                r.Id,
                r.Version,
                r.CreatedAt,
                r.Notes,
                r.RepoId,
                r.CustomProperties
            })
            .FirstOrDefaultAsync(ct);

        if (releaseMeta is null)
            return null;

        var user = _httpContextAccessor.HttpContext?.User
                   ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, releaseMeta.RepoId, RepositoryPermission.Read);

        return new ReleaseGql
        {
            Id = releaseMeta.Id,
            Version = releaseMeta.Version,
            CreatedAt = releaseMeta.CreatedAt,
            Notes = releaseMeta.Notes,
            RepoId = releaseMeta.RepoId,
            CustomProperties = ParseJsonOrNull(releaseMeta.CustomProperties)
        };
    }

    public async Task<ReleaseMetricsGql?> GetReleaseMetricsForReleaseIdAsync(Guid releaseId, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        var releaseMeta = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Id == releaseId && r.Repository.TenantId == tenantContext.TenantId)
            .Select(r => new
            {
                r.Id,
                r.RepoId
            })
            .FirstOrDefaultAsync(ct);
        
        if (releaseMeta is null)
            return null;
        
        await GraphQlAuth.EnsureRepositoryPermissionAsync(user, _authorizationService, tenantContext.TenantId, releaseMeta.RepoId, RepositoryPermission.Read);

        var releaseMetrics = await _db.ReleaseMetrics
            .AsNoTracking()
            .Where(r => r.ReleaseId == releaseId)
            .Select(r => new ReleaseMetricsGql
            {
                ChunksInRelease = r.ChunksInRelease,
                ComponentsInRelease = r.ComponentsInRelease,
                CompressionSavedBytes = r.CompressionSavedBytes,
                DeduplicationSavedBytes = r.DeduplicationSavedBytes,
                FilesInRelease = r.FilesInRelease,
                IncrementalCompressionRatio = r.IncrementalCompressionRatio,
                IncrementalDeduplicationRatio = r.IncrementalDeduplicationRatio,
                IncrementalEffectiveRatio = r.IncrementalEffectiveRatio,
                MetaBytesFull = r.MetaBytesFull,
                NewChunks = r.NewChunks,
                NewCompressedBytes = r.NewCompressedBytes,
                NewDataPercent = r.NewDataPercent,
                NewUniqueLogicalBytes = r.NewUniqueLogicalBytes,
                TotalLogicalBytes = r.TotalLogicalBytes
            })
            .FirstOrDefaultAsync(ct);

        if (releaseMetrics is null)
            return null;
        
        return releaseMetrics;
    }

    private static object? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonDocument.Parse(json);
        }
        catch
        {
            return json;
        }
    }
}