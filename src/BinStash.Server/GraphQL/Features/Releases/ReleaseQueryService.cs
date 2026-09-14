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

namespace BinStash.Server.GraphQL.Features.Releases;

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

        // One metrics row per variant, so a release-level figure is the sum across its targets.
        // Chunk and file counts are summed rather than deduplicated: doing it properly would mean
        // reading every variant's definition, and these are display figures. They therefore read
        // high for a multi-target release, which is the honest direction for a "what did this
        // release contain" number and is never used for billing.
        var perVariant = await _db.ReleaseMetrics
            .AsNoTracking()
            .Where(m => m.Variant.ReleaseId == releaseId)
            .Select(m => new
            {
                m.ChunksInRelease,
                m.ComponentsInRelease,
                m.FilesInRelease,
                m.MetaBytesFull,
                m.TotalLogicalBytes
            })
            .ToListAsync(ct);

        if (perVariant.Count == 0)
            return null;

        return new ReleaseMetricsGql
        {
            ChunksInRelease = perVariant.Sum(x => x.ChunksInRelease),
            ComponentsInRelease = perVariant.Sum(x => x.ComponentsInRelease),
            FilesInRelease = perVariant.Sum(x => x.FilesInRelease),
            MetaBytesFull = perVariant.Sum(x => x.MetaBytesFull),
            TotalLogicalBytes = (ulong)perVariant.Sum(x => (decimal)x.TotalLogicalBytes)
        };
    }

    /// <summary>
    /// Parses stored custom properties into a flat, ordered key/value list.
    /// </summary>
    /// <remarks>
    /// Nested objects and arrays are re-serialised to compact JSON rather than dropped, so
    /// nothing published is silently lost.
    /// </remarks>
    private static List<ReleaseCustomPropertyGql>? ParseJsonOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [new ReleaseCustomPropertyGql { Key = "value", Value = json }];

            return document.RootElement.EnumerateObject()
                .Select(property => new ReleaseCustomPropertyGql
                {
                    Key = property.Name,
                    Value = Stringify(property.Value)
                })
                .ToList();
        }
        catch (JsonException)
        {
            // Not valid JSON — surface it raw rather than failing the whole query.
            return [new ReleaseCustomPropertyGql { Key = "value", Value = json }];
        }
    }

    private static string Stringify(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => element.GetRawText(),
        };
}
