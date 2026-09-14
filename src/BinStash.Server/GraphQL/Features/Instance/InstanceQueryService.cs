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
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BinStash.Server.GraphQL.Features.Instance;

public sealed class InstanceQueryService
{
    internal const string SecretMask = "****";

    private readonly BinStashDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly IOptionsMonitor<GarbageCollectionOptions> _gcOptions;

    public InstanceQueryService(
        BinStashDbContext db,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService,
        IOptionsMonitor<GarbageCollectionOptions> gcOptions)
    {
        _db = db;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _gcOptions = gcOptions;
    }

    public async Task<InstanceStatsGql> GetInstanceStatsAsync(CancellationToken cancellationToken)
    {
        await EnsureAdminAsync();

        // One latest snapshot per chunk store. Physical footprint and free space are only
        // measurable by walking the store, so they come from the hourly collector rather than
        // from a live count — an instance dashboard must not trigger a filesystem sweep.
        //
        // Written as a correlated subquery per store rather than GroupBy(...).First(): the
        // latter is not reliably translatable, and when it falls back it does so by materialising
        // every snapshot ever taken.
        var latestSnapshots = await _db.ChunkStores
            .AsNoTracking()
            .Select(cs => new
            {
                cs.Name,
                Latest = _db.ChunkStoreStatsSnapshots
                    .Where(s => s.ChunkStoreId == cs.Id)
                    .OrderByDescending(s => s.CollectedAt)
                    .Select(s => new { s.PhysicalBytesTotal, s.VolumeFreeBytes })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Volume capacity of zero means the backend could not report it; treating that as "no
        // free space" would raise a permanent false alarm on exactly the backends that cannot
        // answer, so those stores are left out of the headroom figure entirely.
        var tightest = latestSnapshots
            .Where(s => s.Latest is not null && s.Latest.VolumeFreeBytes > 0)
            .OrderBy(s => s.Latest!.VolumeFreeBytes)
            .FirstOrDefault();

        return new InstanceStatsGql
        {
            UserCount = await _db.Users.CountAsync(cancellationToken),
            TenantCount = await _db.Tenants.CountAsync(cancellationToken),
            RepositoryCount = await _db.Repositories.CountAsync(cancellationToken),
            ReleaseCount = await _db.Releases.CountAsync(cancellationToken),
            ChunkStoreCount = await _db.ChunkStores.CountAsync(cancellationToken),
            // Summed as decimal because the column is unsigned: the same cast the billing
            // snapshot uses, and the only one Npgsql will translate here.
            TotalLogicalBytes = (long)await _db.ReleaseMetrics
                .AsNoTracking()
                .SumAsync(m => (decimal)m.TotalLogicalBytes, cancellationToken),
            TotalPhysicalBytes = latestSnapshots.Sum(s => s.Latest?.PhysicalBytesTotal ?? 0),
            MinVolumeFreeBytes = tightest?.Latest?.VolumeFreeBytes,
            MinVolumeFreeChunkStoreName = tightest?.Name
        };
    }

    public async Task<EmailConfigGql> GetEmailConfigAsync()
    {
        await EnsureAdminAsync();

        var brevoApiKey = _configuration["Email:Brevo:ApiKey"];
        var smtpPassword = _configuration["Email:Smtp:Password"];
        var portRaw = _configuration["Email:Smtp:Port"];

        return new EmailConfigGql
        {
            Provider = _configuration["Email:Provider"],
            Shared = new EmailSharedConfigGql
            {
                FromEmail = _configuration["Email:Shared:FromEmail"],
                SupportEmail = _configuration["Email:Shared:SupportEmail"]
            },
            Brevo = new EmailBrevoConfigGql
            {
                ApiKey = string.IsNullOrEmpty(brevoApiKey) ? null : SecretMask
            },
            Smtp = new EmailSmtpConfigGql
            {
                Host = _configuration["Email:Smtp:Host"],
                Port = int.TryParse(portRaw, out var port) ? port : null,
                Username = _configuration["Email:Smtp:Username"],
                Password = string.IsNullOrEmpty(smtpPassword) ? null : SecretMask,
                Security = _configuration["Email:Smtp:Security"]
            }
        };
    }

    public async Task<TenancyConfigGql> GetTenancyConfigAsync()
    {
        await EnsureAdminAsync();
        return new TenancyConfigGql
        {
            Mode = _configuration["Tenancy:Mode"],
            DefaultTenantId = _configuration["Tenancy:DefaultTenantId"]
        };
    }

    public async Task<DomainConfigGql> GetDomainConfigAsync()
    {
        await EnsureAdminAsync();
        return new DomainConfigGql
        {
            BaseUrl = _configuration["Domain:BaseUrl"]
        };
    }

    public async Task<GcConfigGql> GetGcConfigAsync()
    {
        await EnsureAdminAsync();

        // Bound through the options pipeline rather than read key by key, so what is reported is
        // what the scheduler will actually act on — including defaults for keys nobody has set.
        var options = _gcOptions.CurrentValue;
        var schedule = options.Schedule;

        return new GcConfigGql
        {
            Enabled = schedule.Enabled,
            IntervalHours = schedule.Interval.TotalHours,
            WindowStartHourUtc = schedule.WindowStartHourUtc,
            WindowEndHourUtc = schedule.WindowEndHourUtc,
            DryRun = schedule.DryRun,
            SkipReclaim = schedule.SkipReclaim,
            RetentionHours = options.RetentionWindow.TotalHours,
            NextEligibleAt = NextWindowOpening(schedule, DateTimeOffset.UtcNow)
        };
    }

    /// <summary>
    /// The next instant the window admits a run — now, if it already does. Answers "is the
    /// schedule idle or merely waiting?", which is otherwise indistinguishable in the UI.
    /// </summary>
    private static DateTimeOffset? NextWindowOpening(GcScheduleOptions schedule, DateTimeOffset utcNow)
    {
        if (!schedule.Enabled)
            return null;

        if (schedule.IsWithinWindow(utcNow))
            return utcNow;

        // The window is hour-granular, so stepping hour by hour terminates within a day.
        var probe = new DateTimeOffset(utcNow.UtcDateTime, TimeSpan.Zero).AddHours(1);
        probe = new DateTimeOffset(probe.Year, probe.Month, probe.Day, probe.Hour, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 24; i++, probe = probe.AddHours(1))
        {
            if (schedule.IsWithinWindow(probe))
                return probe;
        }

        return null;
    }

    private async Task EnsureAdminAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
    }
}
