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

using BinStash.Core.Auth.Instance;
using BinStash.Core.Auth.Tenant;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Traffic;

/// <summary>
/// Reads the recorded traffic series.
/// </summary>
/// <remarks>
/// The window is clamped rather than trusted. A caller asking for five years of hourly buckets
/// would get a query over the whole table and a response no chart can draw, on a schema that
/// hands filtering to every authenticated tenant.
/// </remarks>
public sealed class TrafficQueryService
{
    /// <summary>
    /// Most buckets one request may return — about three months of hourly, or five years of daily.
    /// </summary>
    private const int MaxBuckets = 2200;

    private readonly BinStashDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public TrafficQueryService(BinStashDbContext db, IHttpContextAccessor httpContextAccessor, IAuthorizationService authorizationService)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    /// <summary>Traffic for the tenant the request resolved to.</summary>
    public async Task<TrafficSeriesGql> GetTenantTrafficAsync(TrafficGrainGql grain, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var tenantContext = GraphQlAuth.EnsureTenantResolved(_httpContextAccessor);
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");

        await GraphQlAuth.EnsureTenantPermissionAsync(user, _authorizationService, tenantContext.TenantId, TenantPermission.Member);

        var (from, to) = ResolveWindow(grain, fromUtc, toUtc);

        var points = await QueryPoints(grain, from, to)
            .Where(x => x.TenantId == tenantContext.TenantId)
            .GroupBy(x => x.BucketStartUtc)
            .Select(g => new
            {
                BucketStartUtc = g.Key,
                IngressBytes = g.Sum(x => x.IngressBytes),
                EgressBytes = g.Sum(x => x.EgressBytes),
                RequestCount = g.Sum(x => x.RequestCount)
            })
            .OrderBy(x => x.BucketStartUtc)
            .ToListAsync(ct);

        return BuildSeries(grain, from, to, points.Select(p => new TrafficPointGql
        {
            BucketStartUtc = p.BucketStartUtc,
            IngressBytes = p.IngressBytes,
            EgressBytes = p.EgressBytes,
            RequestCount = p.RequestCount
        }).ToList());
    }

    /// <summary>Traffic across the whole instance, with a per-tenant split.</summary>
    public async Task<InstanceTrafficGql> GetInstanceTrafficAsync(TrafficGrainGql grain, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);

        var (from, to) = ResolveWindow(grain, fromUtc, toUtc);

        var totals = await QueryPoints(grain, from, to)
            .GroupBy(x => x.BucketStartUtc)
            .Select(g => new
            {
                BucketStartUtc = g.Key,
                IngressBytes = g.Sum(x => x.IngressBytes),
                EgressBytes = g.Sum(x => x.EgressBytes),
                RequestCount = g.Sum(x => x.RequestCount)
            })
            .OrderBy(x => x.BucketStartUtc)
            .ToListAsync(ct);

        var byTenant = await QueryPoints(grain, from, to)
            .GroupBy(x => x.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                IngressBytes = g.Sum(x => x.IngressBytes),
                EgressBytes = g.Sum(x => x.EgressBytes),
                RequestCount = g.Sum(x => x.RequestCount)
            })
            .ToListAsync(ct);

        // Names resolved separately: a tenant deleted since the traffic was recorded still has
        // rows, and dropping them would make the per-tenant split disagree with the total.
        var tenantIds = byTenant.Select(x => x.TenantId).ToList();
        var names = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return new InstanceTrafficGql
        {
            Total = BuildSeries(grain, from, to, totals.Select(p => new TrafficPointGql
            {
                BucketStartUtc = p.BucketStartUtc,
                IngressBytes = p.IngressBytes,
                EgressBytes = p.EgressBytes,
                RequestCount = p.RequestCount
            }).ToList()),
            ByTenant = byTenant
                .OrderByDescending(x => x.IngressBytes + x.EgressBytes)
                .Select(x => new TenantTrafficTotalGql
                {
                    TenantId = x.TenantId,
                    TenantName = names.TryGetValue(x.TenantId, out var name) ? name : "(deleted workspace)",
                    IngressBytes = x.IngressBytes,
                    EgressBytes = x.EgressBytes,
                    RequestCount = x.RequestCount
                })
                .ToList()
        };
    }

    private IQueryable<TenantTrafficSample> QueryPoints(TrafficGrainGql grain, DateTimeOffset from, DateTimeOffset to)
    {
        var storedGrain = grain == TrafficGrainGql.Daily ? TrafficGrain.Daily : TrafficGrain.Hourly;

        return _db.TenantTrafficSamples
            .AsNoTracking()
            .Where(x => x.Grain == storedGrain && x.BucketStartUtc >= from && x.BucketStartUtc < to);
    }

    private static TrafficSeriesGql BuildSeries(TrafficGrainGql grain, DateTimeOffset from, DateTimeOffset to, List<TrafficPointGql> points)
        => new()
        {
            Grain = grain,
            FromUtc = from,
            ToUtc = to,
            Points = points,
            TotalIngressBytes = points.Sum(p => p.IngressBytes),
            TotalEgressBytes = points.Sum(p => p.EgressBytes),
            TotalRequestCount = points.Sum(p => p.RequestCount)
        };

    /// <summary>
    /// Normalizes the requested window: defaults suited to the grain, aligned to bucket
    /// boundaries, and clamped so one request cannot ask for the entire table.
    /// </summary>
    private static (DateTimeOffset From, DateTimeOffset To) ResolveWindow(TrafficGrainGql grain, DateTimeOffset? fromUtc, DateTimeOffset? toUtc)
    {
        var bucket = grain == TrafficGrainGql.Daily ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
        var now = DateTimeOffset.UtcNow;

        // Exclusive upper bound one bucket past now, so the bucket in progress is included.
        var to = Align(toUtc?.ToUniversalTime() ?? now, bucket) + bucket;
        var from = Align(fromUtc?.ToUniversalTime() ?? to - (grain == TrafficGrainGql.Daily ? TimeSpan.FromDays(30) : TimeSpan.FromDays(7)), bucket);

        if (from >= to)
            from = to - bucket;

        var span = to - from;
        if (span.Ticks / bucket.Ticks > MaxBuckets)
            from = to - bucket * MaxBuckets;

        return (from, to);
    }

    private static DateTimeOffset Align(DateTimeOffset value, TimeSpan bucket)
    {
        var utc = value.ToUniversalTime();
        return bucket == TimeSpan.FromDays(1)
            ? new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
