// Copyright (C) 2025  Lukas Eßmann
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

using BinStash.Cli.Infrastructure;
using CliFx;
using CliFx.Attributes;
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

        await ExecuteCommandAsync(console);
    }

    protected abstract ValueTask ExecuteCommandAsync(IConsole console);
}

public abstract class UrlCommandBase : CommandBase
{
    [CommandOption("url", 'u', Description = "The URL to the BinStash server.", IsRequired = true)]
    public string? Url { get; set; }

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
    [CommandOption("token", 't', Description = "Authentication token for the BinStash server.")]
    public string? Token { get; set; }

    [CommandOption("no-auth", 'n', Description = "Disable authentication.")]
    public bool NoAuth { get; set; }

    protected Func<string> AuthTokenFactory { get; private set; } = () => string.Empty;

    protected override async ValueTask<bool> PreCheckAsync(IConsole console)
    {
        if (!await base.PreCheckAsync(console))
            return false;
        
        if (NoAuth) return true;

        var http = new HttpClient { BaseAddress = new Uri(GetUrl()) };
        var auth = new Auth.AuthService(http);

        try
        {
            var accessToken = await auth.GetValidAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                await console.Output.WriteLineAsync("Using stored authentication token.");
                Token = accessToken;
                AuthTokenFactory = () => auth.GetValidAccessTokenAsync().Result;
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
    [CommandOption("tenant", 't', Description = "The tenant slug to operate on.", IsRequired = true)]
    public string? TenantSlug { get; set; }
    
    protected Guid TenantId { get; private set; }
    
    protected new string GetUrl()
    {
        var url = base.GetUrl();
        return $"{url}tenants/{TenantId}/";
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

        var apiClient = new BinStashApiClient(base.GetUrl(), AuthTokenFactory);
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

        return true;
    }
}