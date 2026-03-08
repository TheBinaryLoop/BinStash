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
using System.Text.Json;
using BinStash.Core.Auth.Instance;
using BinStash.Infrastructure.Data;
using BinStash.Server.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BinStash.Server.Endpoints;

public static class InstanceEndpoints
{
    public static RouteGroupBuilder MapInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instance")
            .WithTags("Instance")
            .RequireInstancePermissioin(InstancePermission.Admin);

        //group.MapGet("/info", GetInstanceInfo).WithName("GetInstanceInfo");
        group.MapGet("/stats", GetInstanceStats)
            .WithName("GetInstanceStats")
            .WithDescription("Gets various statistics about the instance, such as storage usage, number of repositories, etc.");

        var configGroup = group.MapGroup("/config")
            .WithTags("Instance", "Configuration");
        
        configGroup.MapGet("/email", GetEmailConfig)
            .WithName("GetEmailConfig")
            .WithDescription("Gets the email configuration of the instance, such as SMTP server, port, etc.");
        configGroup.MapPut("/email", SetEmailConfigAsync)
            .WithName("SetEmailConfig")
            .WithDescription("Sets the email configuration of the instance. Accepts a JSON body with the same format as the output of GetEmailConfig");
        
        return group;
    }
    
    private static async Task<IResult> GetInstanceStats(HttpContext context, BinStashDbContext db)
    {
        return Results.Ok(new
        {
            UserCount = await db.Users.CountAsync(),
            TenantCount = await db.Tenants.CountAsync(),
            RepositoryCount = await db.Repositories.CountAsync()
        });
    }
    
    private static IResult GetEmailConfig(IConfiguration config)
    {
        // Get email configuration from appsettings and return it as JSON. Do not return sensitive information such as passwords/api keys.
        // Needs to work with all email settings regardless of the email provider (SMTP, SendGrid, etc.) and should only return non-sensitive information such as server, port, etc.
        // Config is in the form of Email:[Provider]:[Setting], e.g. Email:Smtp:Server, Email:SendGrid:ApiKey, etc. but also Email:Provider (which specifies the default provider to use)
        var emailConfig = new Dictionary<string, object?>();
        var emailSection = config.GetSection("Email");
        foreach (var provider in emailSection.GetChildren())
        {
            if (provider.Key == "Provider")
            {
                emailConfig[provider.Key] = provider.Value;
                continue;
            }
            
            var providerConfig = new Dictionary<string, object?>();
            foreach (var setting in provider.GetChildren())
            {
                // Check if the setting is sensitive (contains "password", "secret", "key", "token" in the name) and skip it if it is
                if (setting.Key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                    setting.Key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                    setting.Key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                    setting.Key.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    providerConfig[setting.Key] = "****";
                }
                else
                {
                    providerConfig[setting.Key] = setting.Value;
                }
            }
            emailConfig[provider.Key] = providerConfig;
        }
        return Results.Ok(emailConfig);
    }
    
    private static async Task<IResult> SetEmailConfigAsync(HttpContext context, IConfiguration config)
    {
        // Get the JSON body from the request and deserialize it into a dictionary. The JSON should be in the same format as the output of GetEmailConfig.
        using var doc = await JsonDocument.ParseAsync(context.Request.Body);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return Results.BadRequest("Invalid email configuration.");

        foreach (var providerProp in root.EnumerateObject())
        {
            var providerName = providerProp.Name;
            var providerValue = providerProp.Value;

            if (providerName == "Provider")
            {
                config["Email:Provider"] = providerValue.ValueKind == JsonValueKind.Null
                    ? null
                    : providerValue.GetString();
                continue;
            }

            if (providerValue.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var settingProp in providerValue.EnumerateObject())
            {
                var settingName = settingProp.Name;
                var settingValueEl = settingProp.Value;

                // Skip sensitive placeholders
                if (settingValueEl.ValueKind == JsonValueKind.String &&
                    settingValueEl.GetString() == "***")
                {
                    continue;
                }

                config[$"Email:{providerName}:{settingName}"] = JsonElementToString(settingValueEl);
            }
        }

        return Results.Ok("Email configuration updated successfully.");
    }
    
    private static string? JsonElementToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var i) ? i.ToString() : el.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            // For objects/arrays, store raw JSON
            JsonValueKind.Object or JsonValueKind.Array => el.GetRawText(),
            _ => el.ToString()
        };
    }
}