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
using BinStash.Infrastructure.Data;
using BinStash.Server.GraphQL.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.GraphQL.Features.Instance;

public sealed class InstanceQueryService
{
    internal const string SecretMask = "****";

    private readonly BinStashDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthorizationService _authorizationService;

    public InstanceQueryService(
        BinStashDbContext db,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IAuthorizationService authorizationService)
    {
        _db = db;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _authorizationService = authorizationService;
    }

    public async Task<InstanceStatsGql> GetInstanceStatsAsync(CancellationToken cancellationToken)
    {
        await EnsureAdminAsync();
        return new InstanceStatsGql
        {
            UserCount = await _db.Users.CountAsync(cancellationToken),
            TenantCount = await _db.Tenants.CountAsync(cancellationToken),
            RepositoryCount = await _db.Repositories.CountAsync(cancellationToken)
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

    private async Task EnsureAdminAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User ?? throw new GraphQLException("No user context.");
        await GraphQlAuth.EnsureInstancePermissionAsync(user, _authorizationService, InstancePermission.Admin);
    }
}
