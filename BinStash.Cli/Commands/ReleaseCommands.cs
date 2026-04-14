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

using System.Formats.Tar;
using BinStash.Cli.Infrastructure;
using BinStash.Contracts.Release;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using ZstdNet;

namespace BinStash.Cli.Commands;

[Command("release", Description = "Manage releases")]
public partial class ReleasesRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'releases'. Available subcommands: add, list, remove, show.", showHelp: true);
    }
}

[Command("release list", Description = "List all releases you have access to")]
public partial class ReleasesListCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository to query")]
    public required string RepositoryName { get; set; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);
        var repositories = await client.GetRepositoriesAsync();
        if (repositories == null || repositories.Count == 0)
        {
            console.WriteLine("No repositories found. Please create a repository first.");
            return;
        }
        
        var repository = repositories.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository == null)
        {
            console.WriteLine($"Repository '{RepositoryName}' not found. Available repositories:");
            foreach (var repo in repositories)
            {
                console.WriteLine($"- {repo.Name} (ID: {repo.Id})");
            }
            return;
        }
                
        var releases = await client.GetReleasesForRepoAsync(repository.Id);
        releases ??= new List<ReleaseSummaryDto>();
        if (releases.Count == 0)
        {
            await console.Output.WriteLineAsync("No releases found.");
            return;
        }
        releases.Sort((x, y) => y.CreatedAt.CompareTo(x.CreatedAt));
        await console.Output.WriteLineAsync("Available releases:");
        foreach (var release in releases)
        {
            await console.Output.WriteLineAsync($"- {release.Version} created at {release.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ID: {release.Id})");
        }
    }
}

[Command("release delete", Description = "Delete a release")]
public partial class ReleaseDeleteCommand : TenantCommandBase
{
    [CommandOption("id", 'i', Description = "Id of the release")]
    public required Guid ReleaseId { get; set; }

    protected override ValueTask ExecuteCommandAsync(IConsole console)
    {
        throw new NotImplementedException();
    }
}

[Command("release download", Description = "Download a release")]
public partial class ReleaseDownloadCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository for the release")]
    public required string RepositoryName { get; set; } = string.Empty;
    
    [CommandOption("version", 'v', Description = "The version/name of the release")]
    public required string Version { get; set; } = string.Empty;
    
    [CommandOption("component", 'c', Description = "The component to install")]
    public string Component { get; set; } = string.Empty;
    
    [CommandOption("target-folder", 'f', Description = "The target folder to download the release to")]
    public required string TargetFolder { get; set; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        if (string.IsNullOrWhiteSpace(RepositoryName)) throw new CommandException("You must specify a repository name.");
        if (string.IsNullOrWhiteSpace(Version)) throw new CommandException("You must specify either a version to install.");
        if (!string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(RepositoryName)) throw new CommandException("You must specify a repository name when providing a version.");
        if (string.IsNullOrWhiteSpace(TargetFolder)) throw new CommandException("You must specify a target directory.");
        
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);

        var repositories = await client.GetRepositoriesAsync();
        if (repositories == null || repositories.Count == 0)
        {
            console.WriteLine("No repositories found. Please create a repository first.");
            return;
        }
    
        var repository = repositories.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository == null)
        {
            console.WriteLine($"Repository '{RepositoryName}' not found. Available repositories:");
            foreach (var repo in repositories)
            {
                console.WriteLine($"- {repo.Name} (ID: {repo.Id})");
            }
            return;
        }
            
        var releases = await client.GetReleasesForRepoAsync(repository.Id);
        if (releases == null || releases.Count == 0)
        {
            await console.Output.WriteLineAsync("No releases found.");
            return;
        }
        
        var release = releases.FirstOrDefault(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        if (release == null)
        {
            console.WriteLine($"Release with version '{Version}' not found in repository '{RepositoryName}'.");
            return;
        }
        
        // Ensure the target folder exists
        if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
        
        // Download the release package
        await console.Output.WriteLineAsync($"Downloading release '{release.Version}' (ID: {release.Id}) to '{TargetFolder}'...");
        
        var downloadPath = Path.Combine(TargetFolder, $"{release.Version}.tar.zst");

        if (!await client.DownloadReleaseAsync(repository.Id, release.Id, downloadPath, Component))
        {
            throw new CommandException($"Failed to download release '{release.Version}' (ID: {release.Id}) from repository '{release.Repository.Name}'.");
        }
        
        await console.Output.WriteLineAsync($"Successfully downloaded release package to '{TargetFolder}'...");
    }
}

/*[Command("chunk-store show", Description = "Display infos about a chunk store")]
public class ChunkStoreShowCommand : ICommand
{
    [CommandOption("id", 'i', Description = "Id of the chunk store", IsRequired = true)]
    public Guid Id { get; set; }
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var client = new BinStashApiClient("http://localhost:5190");
        var chunkStore = await client.GetChunkStoreAsync(Id);
        
        if (chunkStore == null)
        {
            await console.Output.WriteLineAsync($"Chunk store with ID {Id} not found.");
            return;
        }
        
        await console.Output.WriteLineAsync("Chunk Store Details:");
        await console.Output.WriteLineAsync($"- Name: {chunkStore.Name}");
        await console.Output.WriteLineAsync($"- Type: {chunkStore.Type}");
        await console.Output.WriteLineAsync($"- Chunker: {chunkStore.Chunker.Type}");
        await console.Output.WriteLineAsync($"  - Min chunk size: {chunkStore.Chunker.MinChunkSize}");
        await console.Output.WriteLineAsync($"  - Avg chunk size: {chunkStore.Chunker.AvgChunkSize}");
        await console.Output.WriteLineAsync($"  - Max chunk size: {chunkStore.Chunker.MaxChunkSize}");
    }
}*/

[Command("release install", Description = "Install a release")]
public partial class ReleaseInstallCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository for the release")]
    public required string RepositoryName { get; set; } = string.Empty;
    
    [CommandOption("version", 'v', Description = "The version/name of the release")]
    public required string Version { get; set; } = string.Empty;
    
    [CommandOption("component", 'c', Description = "The component to install")]
    public string Component { get; set; } = string.Empty;

    
    [CommandOption("target-folder", 'f', Description = "The target folder to install the release to")]
    public required string TargetFolder { get; set; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        if (string.IsNullOrWhiteSpace(RepositoryName)) throw new CommandException("You must specify a repository name.");
        if (string.IsNullOrWhiteSpace(Version)) throw new CommandException("You must specify either a version to install.");
        if (!string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(RepositoryName)) throw new CommandException("You must specify a repository name when providing a version.");
        if (string.IsNullOrWhiteSpace(TargetFolder)) throw new CommandException("You must specify a target directory.");
        
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, console: console);

        var repositories = await client.GetRepositoriesAsync();
        if (repositories == null || repositories.Count == 0)
        {
            console.WriteLine("No repositories found. Please create a repository first.");
            return;
        }
    
        var repository = repositories.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (repository == null)
        {
            console.WriteLine($"Repository '{RepositoryName}' not found. Available repositories:");
            foreach (var repo in repositories)
            {
                console.WriteLine($"- {repo.Name} (ID: {repo.Id})");
            }
            return;
        }
            
        var releases = await client.GetReleasesForRepoAsync(repository.Id);
        if (releases == null || releases.Count == 0)
        {
            await console.Output.WriteLineAsync("No releases found.");
            return;
        }
        
        var release = releases.FirstOrDefault(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        if (release == null)
        {
            console.WriteLine($"Release with version '{Version}' not found in repository '{RepositoryName}'.");
            return;
        }
        
        // Ensure the target folder exists
        if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
        
        // Download the release package
        await console.Output.WriteLineAsync($"Downloading release '{release.Version}' (ID: {release.Id}) to '{TargetFolder}'...");
        
        var downloadPath = Path.Combine(TargetFolder, $"{release.Version}.tar.zst");

        if (!await client.DownloadReleaseAsync(repository.Id, release.Id, downloadPath, Component))
        {
            throw new CommandException($"Failed to download release '{release.Version}' (ID: {release.Id}) from repository '{release.Repository.Name}'.");
        }
        
        await File.WriteAllTextAsync(Path.Combine(TargetFolder, "release-id.txt"), release.Id.ToString());
        
        // Extract the downloaded release package
        await console.Output.WriteLineAsync($"Extracting release package to '{TargetFolder}'...");
        await using (var fsIn = File.OpenRead(downloadPath))
        await using (var decompressor = new DecompressionStream(fsIn))
            await TarFile.ExtractToDirectoryAsync(decompressor, TargetFolder, true);
        
        try
        {
            File.Delete(downloadPath);
            await console.Output.WriteLineAsync($"Temporary file '{downloadPath}' deleted.");
        }
        catch (Exception ex)
        {
            await console.Output.WriteLineAsync($"Failed to delete temporary file '{downloadPath}': {ex.Message}");
        }
        await console.Output.WriteLineAsync($"Release '{release.Version}' (ID: {release.Id}) installed successfully to '{TargetFolder}'.");
    }
}