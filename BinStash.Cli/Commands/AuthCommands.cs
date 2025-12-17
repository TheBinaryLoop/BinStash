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

using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using BinStash.Cli.Auth;
using CliFx.Exceptions;
using Spectre.Console;

namespace BinStash.Cli.Commands;
    
[Command("auth", Description = "Manage authentication to BinStash servers")]
public class AuthRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'auth'. Available subcommands: login, logout, logout-all, list.", showHelp: true);
    }
}

[Command("auth login", Description = "Authenticate to a BinStash server")]
public class AuthLoginCommand : UrlCommandBase
{
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var email = AnsiConsole.Prompt(
            new TextPrompt<string>("Email:")
                .PromptStyle("green")
                .Validate(email => email.Contains("@") ? ValidationResult.Success() : ValidationResult.Error("[red]Invalid email address[/]"))
        );

        var password = AnsiConsole.Prompt(
            new TextPrompt<string>("Password:")
                .PromptStyle("green")
                .Secret()
        );

        var http = new HttpClient { BaseAddress = new Uri(GetUrl()) };
        var auth = new AuthService(http);

        try
        {
            var token = await auth.LoginAsync(email, password);
            await console.Output.WriteLineAsync($"Login successful. Token expires at {token.ExpiresAt:u}");
        }
        catch (Exception ex)
        {
            await console.Error.WriteLineAsync($"Login failed: {ex.Message}");
        }
    }
}

[Command("auth logout", Description = "Logout from a BinStash server")]
public class AuthLogoutCommand : ICommand
{
    [CommandParameter(0, Description = "Server URL (e.g. https://api.example.com)")]
    public string Host { get; set; } = default!;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        await CredentialStore.RemoveAsync(new Uri(Host));
        await console.Output.WriteLineAsync($"Logged out from {Host}");
    }
}

[Command("auth logout-all", Description = "Logout from all servers")]
public class AuthLogoutAllCommand : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        CredentialStore.ClearAll();
        await console.Output.WriteLineAsync("Logged out from all servers.");
    }
}

[Command("auth list", Description = "List authenticated servers")]
public class AuthListCommand : ICommand
{
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var hosts = await CredentialStore.ListHostsAsync();
        if (hosts.Count == 0)
        {
            await console.Output.WriteLineAsync("No authenticated servers.");
            return;
        }
        await console.Output.WriteLineAsync("Authenticated servers:");
        foreach (var (host, token) in hosts)
        {
            await console.Output.WriteLineAsync($"- {host} (expires {token.ExpiresAt:u})");
            await console.Output.WriteLineAsync($"\t- AccessToken: {token.AccessToken}");
            await console.Output.WriteLineAsync($"\t- RefreshToken: {token.RefreshToken}");
        }
    }
}
