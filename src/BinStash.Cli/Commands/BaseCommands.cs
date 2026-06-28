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

using BinStash.Cli.Clients;
using BinStash.Cli.Versioning;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

public abstract class CommandBase : ICommand
{
    protected abstract ValueTask<bool> PreCheckAsync(IConsole console);

    public async ValueTask ExecuteAsync(IConsole console)
    {
        if (!await PreCheckAsync(console)) // TODO: Let precheck return a list of errors instead of a bool
        {
            // Exit early if the check fails
            await console.Error.WriteLineAsync("Pre-checks failed. Aborting.");
            return;
        }

        try
        {
            await ExecuteCommandAsync(console);
        }
        catch (CliVersionIncompatibleException ex)
        {
            await console.Error.WriteLineAsync(
                $"[ERROR] {ex.Message}");
        }
    }

    protected abstract ValueTask ExecuteCommandAsync(IConsole console);
}

public abstract class UrlCommandBase : CommandBase
{
    [CommandOption("url", 'u', Description = "The URL to the BinStash server.")]
    public required string? Url { get; set; }

    protected string GetUrl()
    {
        if (string.IsNullOrWhiteSpace(Url))
            return string.Empty;
        // make sure the url ends with api/
        if (Url.EndsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return Url;
        if (Url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            return $"{Url}/";
        if (Url.EndsWith('/'))
            return $"{Url}api/";
        return $"{Url}/api/";
    }
    
    protected override ValueTask<bool> PreCheckAsync(IConsole console)
    {
        // Default checks that all commands inherit
        if (string.IsNullOrWhiteSpace(Url)) 
            return new(false);
        return new(true);
    }
}   

public abstract class AuthenticatedCommandBase : UrlCommandBase
{
    [CommandOption("token", Description = "Bearer access token (JWT) for the BinStash server.")]
    public string? Token { get; set; }

    [CommandOption("api-key", Description = "Service-account API key in the form <id>.<secret>. Suitable for non-interactive/CI use.")]
    public string? ApiKey { get; set; }

    [CommandOption("no-auth", Description = "Disable authentication.")]
    public bool NoAuth { get; set; }

    protected Func<Task<string>> AuthTokenFactory { get; private set; } = () => Task.FromResult(string.Empty);

    /// <summary>
    /// The HTTP/gRPC authorization scheme that pairs with the token produced by
    /// <see cref="AuthTokenFactory"/> — <c>"Bearer"</c> for JWTs (stored creds or <c>--token</c>)
    /// or <c>"ApiKey"</c> for a service-account API key (<c>--api-key</c>).
    /// </summary>
    protected string AuthScheme { get; private set; } = "Bearer";

    protected override async ValueTask<bool> PreCheckAsync(IConsole console)
    {
        if (!await base.PreCheckAsync(console))
            return false;

        if (NoAuth) return true;

        // Precedence: explicit command-line credentials win over the stored credential store,
        // and are checked first so a missing store (which throws "Not logged in") never aborts
        // a non-interactive/CI invocation that supplied its own credential.

        // 1. Service-account API key — long-lived, ideal for CI. Sent with the "ApiKey" scheme.
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            var apiKey = ApiKey;
            AuthScheme = "ApiKey";
            Token = apiKey;
            AuthTokenFactory = () => Task.FromResult(apiKey);
            return true;
        }

        // 2. Explicit bearer token (raw JWT).
        if (!string.IsNullOrWhiteSpace(Token))
        {
            var token = Token;
            AuthScheme = "Bearer";
            AuthTokenFactory = () => Task.FromResult(token);
            return true;
        }

        // 3. Stored interactive credentials (auto-refreshing).
        var http = new HttpClient { BaseAddress = new Uri(GetUrl()) };
        var auth = new Auth.AuthService(http);

        try
        {
            var accessToken = await auth.GetValidAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                await console.Output.WriteLineAsync("Using stored authentication token.");
                Token = accessToken;
                AuthScheme = "Bearer";
                AuthTokenFactory = auth.GetValidAccessTokenAsync;
            }
        }
        catch (Exception ex)
        {
            await console.Error.WriteLineAsync($"Authentication failed: {ex.Message}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Token))
        {
            await console.Error.WriteLineAsync("The authentication token must be provided.");
            return false;
        }

        return true;
    }
}

public abstract class TenantCommandBase : AuthenticatedCommandBase
{
    [CommandOption("tenant", 't', Description = "The tenant slug to operate on.")]
    public required string? TenantSlug { get; set; }

    private Guid TenantId { get; set; }

    protected Guid GetTenantId() => TenantId;

    /// <summary>
    /// Returns the plain API base URL (e.g. <c>https://host/api/</c>).
    /// Use this for REST endpoints that use host-based tenant resolution
    /// (repositories, releases list, chunk-stores).
    /// </summary>
    protected new string GetUrl() => base.GetUrl();

    /// <summary>
    /// Returns the tenant-scoped API base URL (e.g. <c>https://host/api/tenants/{id}/</c>).
    /// Use this for ingest sessions, release download, and other endpoints that
    /// require the tenant ID embedded in the URL path.
    /// </summary>
    protected string GetTenantUrl()
    {
        var url = base.GetUrl();
        return $"{url}tenants/{TenantId}/";
    }

    /// <summary>
    /// Returns the server root URL (e.g. <c>https://host/</c>) for gRPC channel addressing.
    /// </summary>
    protected string GetGrpcUrl()
    {
        var url = base.GetUrl();
        // Strip the /api/ suffix — gRPC channels use the server root
        if (url.EndsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return url[..^5]; // remove trailing "api/"
        if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            return url[..^4];
        return url;
    }

    protected override async ValueTask<bool> PreCheckAsync(IConsole console)
    {
        if (!await base.PreCheckAsync(console))
            return false;

        if (string.IsNullOrWhiteSpace(TenantSlug))
        {
            await console.Error.WriteLineAsync("The tenant slug must be provided.");
            return false;
        }

        var apiClient = new BinStashApiClient(base.GetUrl(), AuthTokenFactory, authScheme: AuthScheme);
        try
        {
            var tenants = await apiClient.GetTenantsAsync();
            if (tenants is null)
            {
                await console.Error.WriteLineAsync("Failed to retrieve tenants from the server.");
                return false;
            }
            
            var tenant = tenants.FirstOrDefault(t => t.Slug.Equals(TenantSlug, StringComparison.OrdinalIgnoreCase));
            if (tenant is null)
            {
                await console.Error.WriteLineAsync($"The tenant '{TenantSlug}' does not exist or is not accessible with the provided token.");
                return false;
            }
            
            TenantId = tenant.TenantId;
        }
        catch (CliVersionIncompatibleException ex)
        {
            await console.Error.WriteLineAsync($"[ERROR] {ex.Message}");
            return false;
        }

        return true;
    }
}