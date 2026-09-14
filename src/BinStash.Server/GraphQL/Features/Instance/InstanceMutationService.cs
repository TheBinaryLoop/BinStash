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

using System.Globalization;
using BinStash.Core.Auth.Instance;
using BinStash.Server.Email;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;

using BinStash.Core.Auditing;
using BinStash.Core.Entities;

namespace BinStash.Server.GraphQL.Features.Instance;

public sealed class InstanceMutationService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;
    private readonly IConfiguration _configuration;
    private readonly InstanceQueryService _queryService;
    private readonly IInstanceEmailTester _emailTester;
    private readonly ILogger<InstanceMutationService> _logger;
    private readonly IAuditLogWriter _audit;

    public InstanceMutationService(
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService,
        IConfiguration configuration,
        InstanceQueryService queryService,
        IInstanceEmailTester emailTester,
        ILogger<InstanceMutationService> logger,
        IAuditLogWriter audit)
    {
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
        _configuration = configuration;
        _queryService = queryService;
        _emailTester = emailTester;
        _logger = logger;
        _audit = audit;
    }

    public async Task<SendTestEmailResultGql> SendTestEmailAsync(string recipientEmail, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync();

        if (string.IsNullOrWhiteSpace(recipientEmail))
            throw new GraphQLException("A recipient email address is required.");

        try
        {
            await _emailTester.SendTestEmailAsync(recipientEmail, cancellationToken);
            return new SendTestEmailResultGql { Success = true };
        }
        catch (Exception ex)
        {
            // Provider/configuration failures are returned as data (not thrown) so the UI can
            // distinguish a misconfigured provider from a transport/authorization error.
            //
            // Nothing derived from the recipient address is logged. This is an admin-triggered
            // one-off whose failure detail is already returned to the caller in ProviderError,
            // so the address buys nothing here — and logs are shipped and retained on different
            // terms from the database.
            _logger.LogWarning(ex, "Test email failed to send.");
            return new SendTestEmailResultGql { Success = false, ProviderError = ex.Message };
        }
    }

    public async Task<EmailConfigGql> SetEmailConfigAsync(SetEmailConfigInput input)
    {
        await EnsureAdminAsync();

        var updates = new Dictionary<string, string?>();
        var errors = new List<string>();

        if (input.Provider is not null)
        {
            if (string.IsNullOrWhiteSpace(input.Provider))
                errors.Add("Provider must be a non-empty string.");
            else
                updates["Email:Provider"] = input.Provider;
        }

        if (input.Shared is not null)
        {
            AddEmail(updates, errors, "Email:Shared:FromEmail", input.Shared.FromEmail);
            AddEmail(updates, errors, "Email:Shared:SupportEmail", input.Shared.SupportEmail);
        }

        if (input.Brevo is not null && input.Brevo.ApiKey is not null && !IsMask(input.Brevo.ApiKey))
            updates["Email:Brevo:ApiKey"] = input.Brevo.ApiKey;

        if (input.Smtp is not null)
        {
            if (input.Smtp.Host is not null) updates["Email:Smtp:Host"] = input.Smtp.Host;
            if (input.Smtp.Username is not null) updates["Email:Smtp:Username"] = input.Smtp.Username;
            if (input.Smtp.Security is not null) updates["Email:Smtp:Security"] = input.Smtp.Security;
            if (input.Smtp.Password is not null && !IsMask(input.Smtp.Password)) updates["Email:Smtp:Password"] = input.Smtp.Password;
            if (input.Smtp.Port is not null)
            {
                if (input.Smtp.Port is < 1 or > 65535)
                    errors.Add("Port must be a number between 1 and 65535.");
                else
                    updates["Email:Smtp:Port"] = input.Smtp.Port.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        Apply(updates, errors);
        await AuditConfigChangeAsync(AuditActions.InstanceEmailConfigChanged, updates);
        return await _queryService.GetEmailConfigAsync();
    }

    public async Task<TenancyConfigGql> SetTenancyConfigAsync(SetTenancyConfigInput input)
    {
        await EnsureAdminAsync();

        var updates = new Dictionary<string, string?>();
        var errors = new List<string>();

        if (input.Mode is not null)
        {
            if (!string.Equals(input.Mode, "Single", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(input.Mode, "Multi", StringComparison.OrdinalIgnoreCase))
                errors.Add("Mode must be either 'Single' or 'Multi'.");
            else
                updates["Tenancy:Mode"] = input.Mode;
        }

        if (input.DefaultTenantId is not null)
            updates["Tenancy:DefaultTenantId"] = input.DefaultTenantId;

        Apply(updates, errors);
        await AuditConfigChangeAsync(AuditActions.InstanceTenancyConfigChanged, updates);
        return await _queryService.GetTenancyConfigAsync();
    }

    public async Task<DomainConfigGql> SetDomainConfigAsync(SetDomainConfigInput input)
    {
        await EnsureAdminAsync();

        var updates = new Dictionary<string, string?>();
        var errors = new List<string>();

        if (input.BaseUrl is not null)
        {
            if (!Uri.TryCreate(input.BaseUrl, UriKind.Absolute, out _))
                errors.Add("Base URL must be a valid absolute URL.");
            else
                updates["Domain:BaseUrl"] = input.BaseUrl;
        }

        Apply(updates, errors);
        await AuditConfigChangeAsync(AuditActions.InstanceDomainConfigChanged, updates);
        return await _queryService.GetDomainConfigAsync();
    }

    /// <summary>
    /// Changes the unattended collection schedule.
    /// </summary>
    /// <remarks>
    /// Values are written as configuration keys rather than to a dedicated table so the schedule
    /// stays overridable by appsettings and environment variables the same way every other
    /// instance setting is — an operator can pin it in a container image, and the UI edits the
    /// database layer that sits on top.
    /// </remarks>
    public async Task<GcConfigGql> SetGcConfigAsync(SetGcConfigInput input)
    {
        await EnsureAdminAsync();

        var updates = new Dictionary<string, string?>();
        var errors = new List<string>();

        if (input.Enabled is { } enabled)
            updates["ChunkStoreGc:Schedule:Enabled"] = enabled ? "true" : "false";

        if (input.IntervalHours is { } interval)
        {
            // An interval at or below zero would make every tick find every store due, turning
            // the scheduler into a loop that never lets a run finish before queuing the next.
            if (interval <= 0)
                errors.Add("Collection interval must be greater than zero hours.");
            else if (interval > 24 * 365)
                errors.Add("Collection interval must be at most a year.");
            else
                updates["ChunkStoreGc:Schedule:Interval"] = TimeSpan.FromHours(interval).ToString("c", CultureInfo.InvariantCulture);
        }

        if (input.ClearWindow == true)
        {
            updates["ChunkStoreGc:Schedule:WindowStartHourUtc"] = null;
            updates["ChunkStoreGc:Schedule:WindowEndHourUtc"] = null;
        }
        else
        {
            AddHour(updates, errors, "ChunkStoreGc:Schedule:WindowStartHourUtc", input.WindowStartHourUtc);
            AddHour(updates, errors, "ChunkStoreGc:Schedule:WindowEndHourUtc", input.WindowEndHourUtc);
        }

        if (input.DryRun is { } dryRun)
            updates["ChunkStoreGc:Schedule:DryRun"] = dryRun ? "true" : "false";

        if (input.SkipReclaim is { } skipReclaim)
            updates["ChunkStoreGc:Schedule:SkipReclaim"] = skipReclaim ? "true" : "false";

        if (input.RetentionHours is { } retention)
        {
            // Retention is the whole safety margin: it is how long an ingest that was told "you
            // already have this" has to finalise before the object it relied on can be destroyed.
            // Zero is a legitimate operator choice for draining a store, but it is not one to
            // arrive at by leaving a field blank, so it is rejected here rather than defaulted.
            if (retention <= 0)
                errors.Add("Quarantine retention must be greater than zero hours. Collect with an explicit per-run override to reclaim immediately.");
            else if (retention > 24 * 365)
                errors.Add("Quarantine retention must be at most a year.");
            else
                updates["ChunkStoreGc:RetentionWindow"] = TimeSpan.FromHours(retention).ToString("c", CultureInfo.InvariantCulture);
        }

        Apply(updates, errors);
        await AuditConfigChangeAsync(AuditActions.InstanceGcConfigChanged, updates);
        return await _queryService.GetGcConfigAsync();
    }

    private static void AddHour(Dictionary<string, string?> updates, List<string> errors, string key, int? value)
    {
        if (value is null)
            return;

        if (value is < 0 or > 23)
            errors.Add($"{key.Split(':')[^1]} must be a whole hour between 0 and 23.");
        else
            updates[key] = value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private void Apply(Dictionary<string, string?> updates, List<string> errors)
    {
        if (errors.Count > 0)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage(string.Join(" ", errors))
                .SetCode("VALIDATION")
                .Build());

        foreach (var (key, value) in updates)
            _configuration[key] = value;

        if (_configuration is IConfigurationRoot root)
            root.Reload();
    }

    private static void AddEmail(Dictionary<string, string?> updates, List<string> errors, string key, string? value)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            errors.Add($"{key.Split(':')[^1]} must be a valid email address.");
        else
            updates[key] = value;
    }

    private static bool IsMask(string value) => string.Equals(value, InstanceQueryService.SecretMask, StringComparison.Ordinal);

    /// <summary>
    /// Records that instance configuration changed, listing only the configuration KEYS touched.
    /// The values are never recorded: this dictionary carries the SMTP password and the Brevo API
    /// key, and an audit trail is exactly the wrong place to persist a secret in clear text.
    /// </summary>
    private async Task AuditConfigChangeAsync(string action, Dictionary<string, string?> updates)
    {
        if (updates.Count == 0)
            return;

        await _audit.WriteAsync(new AuditEntryDraft
        {
            Action = action,
            InstanceScoped = true,
            TargetType = "InstanceSettings",
            Metadata = new Dictionary<string, object?>
            {
                ["changedKeys"] = updates.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()
            }
        });
    }

    private async Task EnsureAdminAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
    }
}
