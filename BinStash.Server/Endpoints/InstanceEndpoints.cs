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
    private const string SecretMask = "****";
    
    private static readonly ConfigSectionDefinition EmailDefinition = new(
        SectionName: "Email",
        IsSensitive: path => path.Any(IsSensitiveKey),
        Validate: ValidateEmailValue,
        CanWrite: _ => true
    );
    private static readonly ConfigSectionDefinition TenancyDefinition = new(
        SectionName: "Tenancy",
        Validate: ValidateTenancyValue,
        CanWrite: _ => true
    );
    private static readonly ConfigSectionDefinition DomainDefinition = new(
        SectionName: "Domain",
        Validate: ValidateDomainValue,
        CanWrite: _ => true
    );
    
    public static RouteGroupBuilder MapInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/config", GetPublicConfig)!
            .WithName("GetPublicConfig")
            .WithDescription(
                "Gets the public configuration of the instance, such as instance name, description, etc. This should not include any sensitive information or settings that are not relevant to users.")
            .AllowAnonymous();
        
        
        var group = app.MapGroup("/api/instance")!
            .WithTags("Instance")
            .RequireInstancePermission(InstancePermission.Admin);

        //group.MapGet("/info", GetInstanceInfo).WithName("GetInstanceInfo");
        group.MapGet("/stats", GetInstanceStats)!
            .WithName("GetInstanceStats")
            .WithDescription("Gets various statistics about the instance, such as storage usage, number of repositories, etc.");

        var configGroup = group.MapGroup("/config")!
            .WithTags("Instance", "Configuration");
        
        configGroup.MapGet("/email", (IConfiguration config) => GetConfig(config, EmailDefinition))!
            .WithName("GetEmailConfig")
            .WithDescription("Gets the email configuration of the instance, such as SMTP server, port, etc.");
        configGroup.MapPut("/email", (HttpContext context, IConfiguration config) => SetConfigAsync(context, config, EmailDefinition))!
            .WithName("SetEmailConfig")
            .WithDescription("Sets the email configuration of the instance. Accepts a JSON body with the same format as the output of GetEmailConfig");
        
        configGroup.MapGet("/tenancy", (IConfiguration config) => GetConfig(config, TenancyDefinition))!
            .WithName("GetTenancyConfig")
            .WithDescription("Gets the tenancy configuration of the instance, such as whether it's single-tenant or multi-tenant, etc.");
        configGroup.MapPut("/tenancy", (HttpContext context, IConfiguration config) => SetConfigAsync(context, config, TenancyDefinition))!
            .WithName("SetTenancyConfig")
            .WithDescription("Sets the tenancy configuration of the instance. Accepts a JSON body with the same format as the output of GetTenancyConfig");
        
        configGroup.MapGet("/domain", (IConfiguration config) => GetConfig(config, DomainDefinition))!
            .WithName("GetDomainConfig")
            .WithDescription("Gets the domain configuration of the instance, such as custom domain settings, etc.");
        configGroup.MapPut("/domain", (HttpContext context, IConfiguration config) => SetConfigAsync(context, config, DomainDefinition))!
            .WithName("SetDomainConfig")
            .WithDescription("Sets the domain configuration of the instance. Accepts a JSON body with the same format as the output of GetDomainConfig");
        
        
        return group;
    }

    private static async Task<IResult> GetInstanceStats(HttpContext context, BinStashDbContext db)
    {
        var userCountTask = db.Users.CountAsync();
        var tenantCountTask = db.Tenants.CountAsync();
        var repoCountTask = db.Repositories.CountAsync();

        await Task.WhenAll(userCountTask, tenantCountTask, repoCountTask);
        
        return Results.Ok(new
        {
            UserCount = userCountTask.Result,
            TenantCount = tenantCountTask.Result,
            RepositoryCount = repoCountTask.Result
        });
    }
    
    
    private static IResult GetPublicConfig(IConfiguration config)
    {
        // TODO: Stuff like signup enabled, instance name/description, etc. that is not sensitive and can be shown to anonymous users
        return Results.Ok(new
        {
            InstanceMode = config["Instance:Mode"],
            TenancyMode = config["Tenancy:Mode"],
        });
    }
    
    private static IResult GetConfig(IConfiguration config, ConfigSectionDefinition definition)
    {
        var section = config.GetSection(definition.SectionName);
        var result = ReadSection(section, definition, Array.Empty<string>());
        return Results.Ok(result);
    }

    private static async Task<IResult> SetConfigAsync(HttpContext context, IConfiguration config, ConfigSectionDefinition definition)
    {
        using var doc = await JsonDocument.ParseAsync(context.Request.Body);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return Results.BadRequest(new
            {
                Error = $"Invalid {definition.SectionName} configuration."
            });

        var errors = new Dictionary<string, string>();
        var updates = new Dictionary<string, string?>();

        CollectUpdates(
            definition: definition,
            json: root,
            currentPath: [],
            updates: updates,
            errors: errors);

        if (errors.Count > 0)
            return Results.ValidationProblem(errors.ToDictionary(x => x.Key, x => new[] { x.Value }));

        foreach (var update in updates)
        {
            config[update.Key] = update.Value;
        }

        ReloadConfig(config);

        return Results.Ok(new { Message = $"{definition.SectionName} configuration updated successfully." });
    }

    private static object? ReadSection(IConfigurationSection section, ConfigSectionDefinition definition, string[] currentPath)
    {
        var children = section.GetChildren().ToList();
        if (children.Count == 0)
        {
            if (definition.IsSensitive?.Invoke(currentPath) == true)
                return SecretMask;

            return section.Value;
        }

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in children)
        {
            var childPath = currentPath.Concat([child.Key]).ToArray();
            dict[child.Key] = ReadSection(child, definition, childPath);
        }

        return dict;
    }

    private static void CollectUpdates(ConfigSectionDefinition definition, JsonElement json, string[] currentPath, Dictionary<string, string?> updates, Dictionary<string, string> errors)
    {
        foreach (var property in json.EnumerateObject())
        {
            var path = currentPath.Concat([property.Name]).ToArray();
            var fullKey = BuildConfigKey(definition.SectionName, path);

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectUpdates(definition, property.Value, path, updates, errors);
                continue;
            }

            if (definition.CanWrite is not null && !definition.CanWrite(path))
            {
                errors[fullKey] = "This setting cannot be modified.";
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                string.Equals(property.Value.GetString(), SecretMask, StringComparison.Ordinal))
            {
                continue;
            }

            var validationError = definition.Validate?.Invoke(path, property.Value);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                errors[fullKey] = validationError!;
                continue;
            }

            updates[fullKey] = JsonElementToString(property.Value);
        }
    }

    private static string BuildConfigKey(string sectionName, string[] path)
        => string.Join(':', new[] { sectionName }.Concat(path));

    private static string? JsonElementToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var i)
                ? i.ToString(CultureInfo.InvariantCulture)
                : el.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object or JsonValueKind.Array => el.GetRawText(),
            _ => el.ToString()
        };
    }

    private static void ReloadConfig(IConfiguration cfg)
    {
        if (cfg is IConfigurationRoot root)
            root.Reload();
    }

    private static bool IsSensitiveKey(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateEmailValue(string[] path, JsonElement value)
    {
        var last = path[^1];

        if (last.Equals("Port", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var port) || port is < 1 or > 65535)
                return "Port must be a number between 1 and 65535.";
        }

        if (last.Equals("FromEmail", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("SupportEmail", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("Sender", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || !value.GetString()!.Contains('@'))
                return "Must be a valid email address.";
        }

        if (last.Equals("Provider", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                return "Provider must be a non-empty string.";
        }

        return null;
    }

    private static string? ValidateTenancyValue(string[] path, JsonElement value)
    {
        var last = path[^1];

        if (last.Equals("Mode", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.String)
                return "Mode must be a string.";

            var mode = value.GetString();
            if (!string.Equals(mode, "Single", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "Multi", StringComparison.OrdinalIgnoreCase))
            {
                return "Mode must be either 'Single' or 'Multi'.";
            }
        }
        
        // If single tenant, DefaultTenantId must be a valid GUID. If multi tenant, DomainSuffix must be a valid domain suffix (e.g. example.com)

        return null;
    }

    private static string? ValidateDomainValue(string[] path, JsonElement value)
    {
        var last = path[^1];

        if (last.Equals("BaseUrl", StringComparison.OrdinalIgnoreCase) ||
            last.Equals("PublicUrl", StringComparison.OrdinalIgnoreCase))
        {
            if (value.ValueKind != JsonValueKind.String || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out _))
                return "Must be a valid absolute URL.";
        }

        return null;
    }
    
    
    private sealed record ConfigSectionDefinition(
        string SectionName,
        Func<string[], bool>? IsSensitive = null,
        Func<string[], JsonElement, string?>? Validate = null,
        Func<string[], bool>? CanWrite = null
    );
}