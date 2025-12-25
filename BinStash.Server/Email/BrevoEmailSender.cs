// Copyright (C) 2025  Lukas Eßmann
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
using BinStash.Core.Entities;
using BinStash.Infrastructure.Email.Brevo;
using BinStash.Infrastructure.Templates;
using Microsoft.AspNetCore.Identity;

namespace BinStash.Server.Email;

public class BrevoEmailSender : IEmailSender<BinStashUser>, ITenantEmailSender
{
    private readonly ILogger<BrevoEmailSender> _logger;
    private readonly BrevoApiClient _brevoApiClient;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;

    // TODO: Make sender email, name, company/org name configurable and also respect the current tenant for multi-tenancy enabled setups
    
    public BrevoEmailSender(BrevoApiClient brevoApiClient, ILogger<BrevoEmailSender> logger, IEmailTemplateRenderer templateRenderer, IConfiguration configuration)
    {
        _brevoApiClient = brevoApiClient;
        _logger = logger;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
    }

    public Task SendConfirmationLinkAsync(BinStashUser user, string email, string confirmationLink)
    {
        var emailText = _templateRenderer.Render("EmailConfirmation", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ActionUrl = confirmationLink,
            LinkTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            SupportEmail = _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured.")
        });
        
        return _brevoApiClient.SendEmailAsync("BinStash", _configuration["Email:FromEmail"] ?? throw new InvalidOperationException("From email is not configured."), email, "Please confirm your email address", emailText, _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured."));
    }

    public Task SendPasswordResetLinkAsync(BinStashUser user, string email, string resetLink)
    {
        var emailText = _templateRenderer.Render("PasswordResetLink", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ActionUrl = resetLink,
            LinkTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            SupportEmail = _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured.")
        });
        
        return _brevoApiClient.SendEmailAsync("BinStash", _configuration["Email:FromEmail"] ?? throw new InvalidOperationException("From email is not configured."), email, "Reset your password", emailText, _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured."));
    }

    public Task SendPasswordResetCodeAsync(BinStashUser user, string email, string resetCode)
    {
        var emailText = _templateRenderer.Render("PasswordResetCode", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ResetCode = resetCode,
            CodeTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            SupportEmail = _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured.")
        });
        
        return _brevoApiClient.SendEmailAsync("BinStash", _configuration["Email:FromEmail"] ?? throw new InvalidOperationException("From email is not configured."), email, "Reset your password", emailText, _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured."));
    }

    public Task SendMemberInvitationEmailAsync(BinStashUser inviter, Tenant tenant, string email, string invitationLink)
    {
        var emailText = _templateRenderer.Render("TenantInvitation", new
        {
            InviterName = $"{inviter.FirstName} {(!string.IsNullOrEmpty(inviter.MiddleName) ? $"{inviter.MiddleName} " : string.Empty)}{inviter.LastName}",
            InviterEmail = inviter.Email,
            TenantName = tenant.Name,
            TenantSlug = tenant.Slug,
            ActionUrl = invitationLink,
            InviteTtl = "7 days",
            DateTimeOffset.UtcNow.Year,
            SupportEmail = _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured.")
        });
        
        /*
         * userName?
         * roleName
         * inviteCode
         * TODO: Maybe implement handlebars templates for tenant invitations? Maybe even for other emails as well?
         */
        return _brevoApiClient.SendEmailAsync("BinStash", _configuration["Email:FromEmail"] ?? throw new InvalidOperationException("From email is not configured."), email, "Join your team at BinStash", emailText, _configuration["Email:SupportEmail"] ?? throw new InvalidOperationException("Support email is not configured."));

    }
}