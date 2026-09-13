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
            // Only the recipient's DOMAIN is logged. Logs are shipped and retained on different
            // terms from the database, and the full address adds nothing here that the exception
            // does not already give us — while "every @example.com delivery fails" is exactly the
            // pattern worth being able to see.
            _logger.LogWarning(ex, "Test email to a {Domain} address failed.", DomainOf(recipientEmail));
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

    /// <summary>
    /// The domain portion of an email address, for diagnostics that must not carry the
    /// address itself. Returns a placeholder rather than the input when it cannot be parsed,
    /// so a malformed value can never leak through this path either.
    /// </summary>
    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : "(unparseable)";
    }

    private async Task EnsureAdminAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
    }
}
