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
using BinStash.Contracts.Repo;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("repo", Description = "Manage repositories")]
public class RepoRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'repo'. Available subcommands: add, list, remove.", showHelp: true);
    }
}

[Command("repo list", Description = "List all repositories you have access to")]
public class RepoListCommand : TenantCommandBase
{
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);
        var repos = await client.GetRepositoriesAsync();
        if (repos == null || repos.Count == 0)
        {
            await console.Output.WriteLineAsync("No repositories found.");
            return;
        }
        await console.Output.WriteLineAsync("Available repositories:");
        foreach (var repo in repos)
        {
            await console.Output.WriteLineAsync($"- {repo.Name} (ID: {repo.Id})");
        }
    }
}

[Command("repo add", Description = "Add a new repository")]
public class RepoAddCommand : TenantCommandBase
{
    [CommandOption("name", 'n', Description = "Name of the repository", IsRequired = true)]
    public string Name { get; init; } = string.Empty;

    [CommandOption("description", 'd', Description = "Description of the repository")]
    public string? Description { get; init; }
    
    [CommandOption("storage-class", 'c', Description = "Storage class for the repository")]
    public string? StorageClass { get; set; } = string.Empty;
    
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);
        
        
        var createDto = new CreateRepositoryDto
        {
            Name = Name,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            StorageClassName = StorageClass
        };
        
        var repo = await client.CreateRepositoryAsync(createDto);
        
        if (repo == null)
        {
            await console.Output.WriteLineAsync("Failed to create repository. Please check the provided parameters.");
            return;
        }
        
        await console.Output.WriteLineAsync("Repository Details:");
        await console.Output.WriteLineAsync($"- ID: {repo.Id}");
        await console.Output.WriteLineAsync($"- Name: {repo.Name}");
        await console.Output.WriteLineAsync($"- Desc: {repo.Description}");
        await console.Output.WriteLineAsync($"- Storage class: {repo.StorageClass}");
    }
}