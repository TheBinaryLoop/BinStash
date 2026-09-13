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

using BinStash.Core.Auth.Tenant;
using BinStash.Core.Billing;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Usage;

public sealed class UsageQueryService
{
    private readonly BinStashDbContext _db;
    private readonly IBillingProvider _billingProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public UsageQueryService(
        BinStashDbContext db,
        IBillingProvider billingProvider,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _billingProvider = billingProvider;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<TenantUsageGql> GetTenantUsageAsync(CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User
                   ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(
            user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        var tenantId = tenantContext.TenantId;

        // Metrics hang off releases, which hang off repositories, which carry the tenant. Sum in
        // one pass rather than per-repository to keep this O(1) queries regardless of repo count.
        var totals = await _db.ReleaseMetrics
            .AsNoTracking()
            .Join(_db.Releases.AsNoTracking(), rm => rm.ReleaseId, r => r.Id, (rm, r) => new { rm, r.RepoId })
            .Join(_db.Repositories.AsNoTracking(), x => x.RepoId, repo => repo.Id, (x, repo) => new { x.rm, repo.TenantId })
            .Where(x => x.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                LogicalBytes = (long)g.Sum(x => (decimal)x.rm.TotalLogicalBytes),
                StoredBytes = g.Sum(x => x.rm.NewUniqueLogicalBytes),
                CompressedBytes = g.Sum(x => x.rm.NewCompressedBytes),
                DeduplicationSavedBytes = g.Sum(x => x.rm.DeduplicationSavedBytes),
                CompressionSavedBytes = g.Sum(x => x.rm.CompressionSavedBytes),
                ReleaseCount = g.Count()
            })
            .FirstOrDefaultAsync(ct);

        var repositoryCount = await _db.Repositories
            .AsNoTracking()
            .CountAsync(r => r.TenantId == tenantId, ct);

        var limits = await _billingProvider.GetLimitsAsync(tenantId, ct);

        // NoOp billing reports long.MaxValue, which is "no plan limit" rather than a real ceiling.
        var isLimited = limits.MaxStorageBytes is > 0 and < long.MaxValue;
        var compressedBytes = totals?.CompressedBytes ?? 0;

        return new TenantUsageGql
        {
            TenantId = tenantId,
            LogicalBytes = totals?.LogicalBytes ?? 0,
            StoredBytes = totals?.StoredBytes ?? 0,
            CompressedBytes = compressedBytes,
            DeduplicationSavedBytes = totals?.DeduplicationSavedBytes ?? 0,
            CompressionSavedBytes = totals?.CompressionSavedBytes ?? 0,
            ReleaseCount = totals?.ReleaseCount ?? 0,
            RepositoryCount = repositoryCount,
            MaxStorageBytes = isLimited ? limits.MaxStorageBytes : null,
            IsLimited = isLimited,
            StorageUsedFraction = isLimited
                ? (double)compressedBytes / limits.MaxStorageBytes
                : null,
            IsStorageAllowed = limits.IsStorageAllowed,
            IsIngestAllowed = limits.IsIngestAllowed,
            IsEgressAllowed = limits.IsEgressAllowed
        };
    }
}
