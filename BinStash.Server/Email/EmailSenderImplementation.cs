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
using BinStash.Core.Entities;
using BinStash.Infrastructure.Templates;
using BinStash.Server.Configuration;
using BinStash.Server.Email.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BinStash.Server.Email;

public sealed class EmailSenderImplementation : IEmailSender<BinStashUser>, ITenantEmailSender
{
    private readonly ILogger<EmailSenderImplementation> _logger;
    private readonly IOptionsMonitor<EmailSettings> _emailSettings;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IServiceProvider _serviceProvider;

    public EmailSenderImplementation(ILogger<EmailSenderImplementation> logger, IOptionsMonitor<EmailSettings> emailSettings, IEmailTemplateRenderer templateRenderer, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _emailSettings = emailSettings;
        _templateRenderer = templateRenderer;
        _serviceProvider = serviceProvider;
    }
    
    // TODO: Implement a NoOp email sender for use when email is not configured, and make this one throw if the required settings are not present. This way we can avoid having to check for the presence of the settings in every method.
    
    public Task SendConfirmationLinkAsync(BinStashUser user, string email, string confirmationLink)
    {
        var provider = GetEmailProvider();
        
        var emailSettings = _emailSettings.CurrentValue;
        
        var emailText = _templateRenderer.Render("EmailConfirmation", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ActionUrl = confirmationLink,
            LinkTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            emailSettings.Shared!.SupportEmail
        });
        
        return provider.SendEmailAsync("BinStash", emailSettings.Shared!.FromEmail!, email, "Please confirm your email address", emailText, emailSettings.Shared!.SupportEmail!);
    }

    public Task SendPasswordResetLinkAsync(BinStashUser user, string email, string resetLink)
    {
        var provider = GetEmailProvider();
        
        var emailSettings = _emailSettings.CurrentValue;
        
        var emailText = _templateRenderer.Render("PasswordResetLink", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ActionUrl = resetLink,
            LinkTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            emailSettings.Shared!.SupportEmail
        });
        
        return provider.SendEmailAsync("BinStash", emailSettings.Shared!.FromEmail!, email, "Password reset request", emailText, emailSettings.Shared!.SupportEmail!);
    }

    public Task SendPasswordResetCodeAsync(BinStashUser user, string email, string resetCode)
    {
        var provider = GetEmailProvider();
        
        var emailSettings = _emailSettings.CurrentValue;
        
        var emailText = _templateRenderer.Render("PasswordResetCode", new
        {
            UserName = $"{user.FirstName} {(!string.IsNullOrEmpty(user.MiddleName) ? $"{user.MiddleName} " : string.Empty)}{user.LastName}",
            Email = email,
            ResetCode = resetCode,
            CodeTtl = "15 minutes",
            DateTimeOffset.UtcNow.Year,
            emailSettings.Shared!.SupportEmail
        });
        
        return provider.SendEmailAsync("BinStash", emailSettings.Shared!.FromEmail!, email, "Password reset code", emailText, emailSettings.Shared!.SupportEmail!);
    }

    public Task SendMemberInvitationEmailAsync(BinStashUser inviter, Tenant tenant, string email, string invitationLink)
    {
        var provider = GetEmailProvider();
        
        var emailSettings = _emailSettings.CurrentValue;
        
        var emailText = _templateRenderer.Render("TenantInvitation", new
        {
            InviterName = $"{inviter.FirstName} {(!string.IsNullOrEmpty(inviter.MiddleName) ? $"{inviter.MiddleName} " : string.Empty)}{inviter.LastName}",
            InviterEmail = inviter.Email,
            TenantName = tenant.Name,
            TenantSlug = tenant.Slug,
            ActionUrl = invitationLink,
            LinkTtl = "7 days",
            DateTimeOffset.UtcNow.Year,
            emailSettings.Shared!.SupportEmail
        });
        
        /*
         * userName?
         * roleName
         * inviteCode
         * TODO: Maybe implement handlebars templates for tenant invitations? Maybe even for other emails as well?
         */
        
        return provider.SendEmailAsync("BinStash", emailSettings.Shared!.FromEmail!, email, $"You're invited to join {tenant.Name} on BinStash", emailText, emailSettings.Shared!.SupportEmail!);
    }
    
    
    // Helpers
    private IEmailProvider GetEmailProvider()
    {
        var settings = _emailSettings.CurrentValue;

        switch (settings.Provider)
        {
            case "Brevo":
            {
                if (settings.Brevo == null)
                {
                    _logger.LogError("Brevo email provider selected but Brevo settings are missing.");
                    throw new InvalidOperationException("Brevo settings must be provided when using the Brevo email provider.");
                }
                
                EnsureSettingsPresent("Brevo",
                    settings.Shared?.FromEmail,
                    settings.Shared?.SupportEmail,
                    settings.Brevo.ApiKey);

                var provider = _serviceProvider.GetRequiredService<BrevoEmailProvider>();
                provider.Setup(settings.Brevo.ApiKey);
                
                    /*new BrevoEmailProvider(
                    _serviceProvider.GetRequiredService<ILogger<BrevoEmailProvider>>(),
                    _serviceProvider.GetRequiredKeyedService<HttpClient>(typeof(BrevoEmailProvider)));*/
                
                
                return provider;
            }
            case "None":
                _logger.LogWarning("No email provider configured. Emails will not be sent.");
                throw new InvalidOperationException("No email provider configured. Please configure an email provider to enable email functionality.");
            
            default:
                _logger.LogError("Unsupported email provider configured: {Provider}", settings.Provider);
                throw new InvalidOperationException($"Unsupported email provider: {settings.Provider}");
        }
    }
    
    private void EnsureSettingsPresent(string provider, params string?[] requiredFields)
    {
        var missingFields = requiredFields.Where(string.IsNullOrWhiteSpace).ToArray();
        if (missingFields.Length > 0)
        {
            _logger.LogError("{Provider} email provider selected but the following required settings are missing: {MissingFields}", provider, string.Join(", ", missingFields));
            throw new InvalidOperationException($"The following email settings must be provided when using the selected email provider: {string.Join(", ", missingFields)}");
        }
    }
}