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
using BinStash.Server.Services.Usage;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;

namespace BinStash.Server.GraphQL.Features.Usage;

public sealed class UsageQueryService
{
    private readonly TenantUsageService _usage;
    private readonly IBillingProvider _billingProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public UsageQueryService(TenantUsageService usage, IBillingProvider billingProvider, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _usage = usage;
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

        // TenantUsageService owns the definition of both figures, so the number shown here and
        // the number the quota is enforced against cannot diverge. Logical bytes are reported
        // only to show what the tenant is NOT being charged for.
        var totals = await _usage.GetTotalsAsync(tenantId, ct);

        var limits = await _billingProvider.GetLimitsAsync(tenantId, ct);

        // NoOp billing reports long.MaxValue, which is "no plan limit" rather than a real ceiling.
        var isLimited = limits.MaxStorageBytes is > 0 and < long.MaxValue;
        var logicalBytes = totals.LogicalBytes;
        var uniqueLogicalBytes = totals.BillableBytes;

        return new TenantUsageGql
        {
            TenantId = tenantId,
            LogicalBytes = logicalBytes,
            UniqueLogicalBytes = uniqueLogicalBytes,
            DeduplicationSavedBytes = Math.Max(0, logicalBytes - uniqueLogicalBytes),
            FootprintComputedAt = totals.FootprintComputedAt,
            ReleaseCount = totals.ReleaseCount,
            RepositoryCount = totals.RepositoryCount,
            MaxStorageBytes = isLimited ? limits.MaxStorageBytes : null,
            IsLimited = isLimited,
            // Quota is measured against the deduplicated footprint, matching how the tenant is billed.
            StorageUsedFraction = isLimited
                ? (double)uniqueLogicalBytes / limits.MaxStorageBytes
                : null,
            IsStorageAllowed = limits.IsStorageAllowed,
            IsIngestAllowed = limits.IsIngestAllowed,
            IsEgressAllowed = limits.IsEgressAllowed
        };
    }
}
