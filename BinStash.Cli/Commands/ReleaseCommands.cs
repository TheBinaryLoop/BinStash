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

using System.Collections.Concurrent;
using System.Formats.Tar;
using BinStash.Cli.Converters;
using BinStash.Cli.Infrastructure;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using Blake3;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Spectre.Console;
using ZstdNet;

namespace BinStash.Cli.Commands;

[Command("release", Description = "Manage releases")]
public class ReleasesRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'releases'. Available subcommands: add, list, remove, show.", showHelp: true);
    }
}

[Command("release list", Description = "List all releases you have access to")]
public class ReleasesListCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository to query", IsRequired = true)]
    public string RepositoryName { get; set; } = string.Empty;

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

[Command("release add", Description = "Add a new release")]
public class ReleasesAddCommand : TenantCommandBase
{
    [CommandOption("version", 'v', Description = "The version/name of the release", IsRequired = true)]
    public string Version { get; init; } = string.Empty;
    
    [CommandOption("notes", 'n', Description = "Release notes or description")]
    public string Note { get; set; } = string.Empty;
    
    [CommandOption("notes-file", Description = "File containing release notes or description")]
    public string NoteFile { get; set; } = string.Empty;

    [CommandOption("repository", 'r', Description = "Repository for the release", IsRequired = true)]
    public string RepositoryName { get; set; } = string.Empty;
    
    [CommandOption("folder", 'f', Description = "Folder containing the release files", IsRequired = true)]
    public string RootFolder { get; set; } = string.Empty;

    [CommandOption("component-map", 'c', Description = "The path to the component map file.", IsRequired = false)]
    public string ComponentMapFile { get; init; } = string.Empty;
    
    [CommandOption("custom-property", 'p', Description = "Custom property to add to the release. Can be specified multiple times.", IsRequired = false, Converter = typeof(KeyValuePairConverter<string, string>))]
    public Dictionary<string, string> CustomProperties { get; set; } = new();

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        // Input validation
        if (!Directory.Exists(RootFolder))
            throw new CommandException($"The specified folder '{RootFolder}' does not exist or is not a directory.");
        if (!string.IsNullOrEmpty(ComponentMapFile) && !File.Exists(ComponentMapFile))
            throw new CommandException($"The specified component map file '{ComponentMapFile}' does not exist or is not a file.");
        
        var noteContent = string.IsNullOrEmpty(Note) ? null : Note;
        if (!string.IsNullOrEmpty(NoteFile))
        {
            if (!File.Exists(NoteFile))
                throw new CommandException($"The specified note file '{NoteFile}' does not exist or is not a file.");
            noteContent = await File.ReadAllTextAsync(NoteFile);
        }
        
        var releasePackage = new ReleasePackage
        {
            Version = Version,
            Notes = noteContent,
            CustomProperties = CustomProperties,
            Components = new(),
            Chunks = new(),
            Stats = new()
        };
        var fileHashes = new ConcurrentDictionary<Hash32, List<string>>();
        var fileStats = new ConcurrentDictionary<Hash32, long>();
        var fileHashChunkMaps = new ConcurrentDictionary<Hash32, List<ChunkMapEntry>>();
        var fileEntries = new ConcurrentBag<(ReleaseFile File, List<ChunkMapEntry> Entries, Component Component)>();
        
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);
        
        var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
            Out = new AnsiConsoleOutput(console.Output)
        });

        await ansiConsole.Status()
            .AutoRefresh(true)
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Fetching repos...", async ctx =>
            {
                var repositories = await client.GetRepositoriesAsync();
                if (repositories == null || repositories.Count == 0)
                {
                    ansiConsole.WriteLine("No repositories found. Please create a repository first.");
                    return;
                }
                
                WriteLogMessage(ansiConsole, $"Found {repositories.Count} repositories");
                
                ctx.Status("Checking repository name...");
                
                var repository = repositories.FirstOrDefault(r => r.Name.Equals(RepositoryName, StringComparison.OrdinalIgnoreCase));
                if (repository == null)
                {
                    ansiConsole.WriteLine($"Repository '{RepositoryName}' not found. Available repositories:");
                    foreach (var repo in repositories)
                    {
                        ansiConsole.WriteLine($"- {repo.Name} (ID: {repo.Id})");
                    }
                    return;
                }
                
                ctx.Status("Checking release name duplicate...");
                var releases = await client.GetReleasesForRepoAsync(repository.Id);
                if (releases != null && releases.Any(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase)))
                {
                    ansiConsole.MarkupLine($"[red]Release with version '{Version}' already exists in repository '{repository.Name}'. Please choose a different version.[/]");
                    return;
                }
                
                ctx.Status($"Fetching chunker settings for repository '{repository.Name}'...");
                
                repository = await client.GetRepositoryAsync(repository.Id);
                if (repository == null)
                {
                    // This should never happen!!!
                    ansiConsole.WriteLine($"Repository '{RepositoryName}' not found when fetching details.");
                    return;
                }
                
                if (repository.Chunker == null)
                {
                    ansiConsole.MarkupLine($"[red]Repository '{repository.Name}' does not have a chunker configured. Please configure a chunk store with chunker settings first.[/]");
                    return;
                }
                
                WriteLogMessage(ansiConsole, $"Repository [purple]{repository.Name}[/] uses storage class: [bold blue]{repository.StorageClass}[/]");
                
                ctx.Status("Setting up chunker ...");
                var chunker = (Enum.TryParse<ChunkerType>(repository.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc) switch
                {
                    ChunkerType.FastCdc => new FastCdcChunker(repository.Chunker.MinChunkSize!.Value, repository.Chunker.AvgChunkSize!.Value, repository.Chunker.MaxChunkSize!.Value),
                    _ => throw new NotSupportedException($"Unsupported chunker type: {repository.Chunker.Type}")
                };
                
                WriteLogMessage(ansiConsole, $"Using chunker: [purple]{chunker.GetType().Name}[/] with min size [bold blue]{repository.Chunker.MinChunkSize}[/], avg size [bold blue]{repository.Chunker.AvgChunkSize}[/], max size [bold blue]{repository.Chunker.MaxChunkSize}[/]");
                
                ctx.Status("Creating component map...");

                var componentMap = LoadComponentMap(ansiConsole, releasePackage, ComponentMapFile, RootFolder);
                
                WriteLogMessage(ansiConsole, $"Component map contains [bold blue]{componentMap.Count}[/] components");
                
                if (componentMap.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "No components found in the specified folder. Please check the folder path and try again.");
                    return;
                }

                ctx.Status("Processing files...");
                
                var sw = System.Diagnostics.Stopwatch.StartNew();
                
                foreach (var componentMapEntry in componentMap)
                {
                    WriteLogMessage(ansiConsole, $"Processing component: [purple]{componentMapEntry.Value.Name}[/] in folder [bold blue]{Path.Combine(RootFolder, componentMapEntry.Key)}[/]");
#if xDEBUG
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(RootFolder, componentMapEntry.Key), "*.*",
                                 SearchOption.AllDirectories))
#else
                    Parallel.ForEach(Directory.EnumerateFiles(Path.Combine(RootFolder, componentMapEntry.Key), "*.*", SearchOption.AllDirectories), file =>
#endif
                    {
                        // TODO: Make this configurable
                        if (file.Contains(".git") || file.Contains(".svn") || file.Contains(".hg"))
                        {
                            // Skip files in version control directories
                            return;
                        }
                        
                        using var fsIn = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan); // 64 KB buffer
                        var hasher = Hasher.New();
                        Span<byte> buffer = stackalloc byte[65536]; // 64 KB, fits L1/L2 cache well, maybe need to adjust based on the system
                        int bytesRead;
                        while ((bytesRead = fsIn.Read(buffer)) > 0)
                        {
                            hasher.Update(buffer[..bytesRead]);
                        }
                        var fileHash = hasher.Finalize();
                        
                        fileHashes.AddOrUpdate(new Hash32(fileHash.AsSpan()), [file], (key, list) =>
                        {
                            list.Add(file);
                            return list;
                        });
                        fileStats.AddOrUpdate(new Hash32(fileHash.AsSpan()), fsIn.Length, (key, old) => old);

                        var releaseFile = new ReleaseFile
                        {
                            Name = file.Replace(RootFolder, string.Empty).Replace(componentMapEntry.Key, string.Empty).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            Hash = new Hash32(fileHash.AsSpan()),
                            Chunks = new()
                        };

                        fileEntries.Add((releaseFile, [], componentMapEntry.Value));

                        //WriteLogMessage(ansiConsole, $"Processed file [bold blue]{releaseFile.Component}[/]:[purple]{releaseFile.Name}[/](Chunks: [bold blue]{chunkMap.Count}[/])");
#if xDEBUG
                    }
#else
                    });
#endif
                }
                
                foreach (var group in fileEntries.GroupBy(x => x.Component))
                {
                    var component = group.Key;
                    foreach (var (file, _, _) in group)
                    {
                        component.Files.Add(file);
                    }
                }
                
                WriteLogMessage(ansiConsole, $"Processed [bold blue]{fileHashes.Count}[/] files from the specified folder in [darkorange]{sw.Elapsed}[/]");
                
                ctx.Status("Requesting ingest session...");
                var ingestSessionId = await client.CreateIngestSessionAsync(repository.Id, releasePackage.Version);
                WriteLogMessage(ansiConsole, $"Received ingest session ID: [bold blue]{ingestSessionId}[/]");

                
                ctx.Status("Requesting missing file def info from server...");
                var missingFileChecksums = await client.GetMissingFileChecksumsAsync(repository.Id, ingestSessionId, fileHashes.Keys.ToList());
                if (missingFileChecksums.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "All files are already available on the server. No files need to be processed.");
                }
                else
                {
                    WriteLogMessage(ansiConsole, $"Found [bold blue]{missingFileChecksums.Count}[/] missing files that need to be processed");
                    ctx.Status("Processing missing files...");
                    sw.Restart();
                    
                    Parallel.ForEach(missingFileChecksums, fileHash =>
                    {
                        if (!fileHashes.TryGetValue(fileHash, out var paths))
                            return;
                        
                        var file = paths.First();
                        
                        var chunkMap = chunker.GenerateChunkMap(file).ToList();
                        if (chunkMap.Count == 0)
                        {
                            WriteLogMessage(ansiConsole, $"No chunks generated for file: [red]{file}[/]");
                            return;
                        }
                        
                        fileHashChunkMaps.TryAdd(fileHash, chunkMap);
                    });
                    
                    WriteLogMessage(ansiConsole, $"Processed [bold blue]{fileHashChunkMaps.Sum(x => x.Value.Count)}[/] chunks from [bold blue]{fileHashChunkMaps.Count}[/] files from the specified folder in [darkorange]{sw.Elapsed}[/]");
                }
                
                var uniqueChecksums = fileHashChunkMaps.Values.SelectMany(x => x.Select(cme => cme.Checksum)).Distinct().OrderBy(x => x).ToList();
                
                WriteLogMessage(ansiConsole, $"Found [bold blue]{uniqueChecksums.Count}[/] unique chunk checksums across all files");
                
                
                ctx.Status("Requesting missing chunk info from server...");
                var missingChunkChecksums = await client.GetMissingChunkChecksumsAsync(repository.Id, ingestSessionId, uniqueChecksums);
                if (missingChunkChecksums.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "All chunks are already available on the server. No chunks need to be uploaded.");
                }
                else
                {
                    WriteLogMessage(ansiConsole, $"Found [bold blue]{missingChunkChecksums.Count}[/] missing chunks that need to be processed");
                    
                    ctx.Status("Calculating chunk map entries for upload...");
                    sw.Restart();
                    var missingSet = new HashSet<Hash32>(missingChunkChecksums);
                    var selectedEntries = new ConcurrentDictionary<Hash32, ChunkMapEntry>();
                    
                    Parallel.ForEach(fileHashChunkMaps.Values, chunkMap =>
                    {
                        foreach (var entry in chunkMap)
                        {
                            if (missingSet.Contains(entry.Checksum))
                            {
                                selectedEntries.TryAdd(entry.Checksum, entry);
                            }
                        }
                    });

                    var missingChunkEntries = selectedEntries.Values.ToList();
                    WriteLogMessage(ansiConsole, $"Calculated [bold blue]{missingChunkEntries.Count}[/] chunk map entries for upload in [darkorange]{sw.Elapsed}[/]");
                    sw.Stop();
                    
                    ctx.Status("Uploading missing chunks to server...");
                    sw.Restart();
                    await client.UploadChunksAsync(repository.Id, ingestSessionId, chunker, missingChunkEntries, progressCallback: (uploaded, total) => Task.Run(() => ctx.Status($"Uploading missing chunks to chunk store ({uploaded}/{total} ({(double)uploaded / total:P2}))...")));
                    WriteLogMessage(ansiConsole, $"All missing chunks uploaded to the server in [darkorange]{sw.Elapsed}[/]");
                }

                if (missingFileChecksums.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "No missing file definitions to upload.");
                }
                else
                {
                    ctx.Status("Uploading file definitions...");
                    sw.Restart();
                    await client.UploadFileDefinitionsAsync(repository.Id, ingestSessionId, fileHashChunkMaps.ToDictionary(x => x.Key, x => (Chunks: x.Value.Select(v =>  v.Checksum).ToList(), Length: fileStats[x.Key])), progressCallback: (uploaded, total) => Task.Run(() => ctx.Status($"Uploading missing file definitions to chunk store ({uploaded}/{total} ({(double)uploaded / total:P2}))...")));
                    WriteLogMessage(ansiConsole, $"All file definitions uploaded to the chunk store in [darkorange]{sw.Elapsed}[/]");
                }

                
                ctx.Status("Uploading release package...");
                await client.CreateReleaseAsync(ingestSessionId, repository.Id.ToString(), releasePackage);
                
                /*if (release == null)
                {
                    ansiConsole.MarkupLine("[red]Failed to create release. Please check the provided parameters.[/]");
                    return;
                }*/
                ansiConsole.MarkupLine($"[green]Release created successfully! Took {swTotal.Elapsed}...[/]");
                
            });
    }

    private void WriteLogMessage(IAnsiConsole console, string message)
    {
        // This method can be used to write log messages to the console
        // For now, we just write it directly to the output
        console.MarkupLine($"[grey]LOG:[/] {message}[grey]...[/]");
    }
    
    private Dictionary<string, Component> LoadComponentMap(IAnsiConsole console, ReleasePackage release, string? mapFile, string rootFolder)
    {
        var dict = new Dictionary<string, Component>();

        if (!string.IsNullOrWhiteSpace(mapFile))
        {
            var lines = File.ReadAllLines(mapFile);
            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    var folder = parts[0].Trim();
                    var name = parts[1].Trim();
                    var comp = new Component { Name = name, Files = new() };
                    release.Components.Add(comp);
                    dict[folder] = comp;
                }
                else
                {
                    WriteLogMessage(console, $"Invalid component map line: [red]{line}[/]. Expected format: <ComponentFolder>:<ComponentName>");
                }
            }
        }
        else
        {
            WriteLogMessage(console, "No component map file provided. Using folder names as components.");
            foreach (var dir in Directory.GetDirectories(rootFolder).Where(x => !x.Contains(".git") && !x.Contains(".svn") && !x.Contains(".hg")))
            {
                var name = Path.GetFileName(dir);
                var comp = new Component { Name = name, Files = new() };
                release.Components.Add(comp);
                dict[dir] = comp;
            }
        }

        return dict;
    }

}

[Command("release delete", Description = "Delete a release")]
public class ReleaseDeleteCommand : TenantCommandBase
{
    [CommandOption("id", 'i', Description = "Id of the release", IsRequired = true)]
    public Guid ReleaseId { get; init; }

    protected override ValueTask ExecuteCommandAsync(IConsole console)
    {
        throw new NotImplementedException();
    }
}

[Command("release download", Description = "Download a release")]
public class ReleaseDownloadCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository for the release", IsRequired = true)]
    public string RepositoryName { get; init; } = string.Empty;
    
    [CommandOption("version", 'v', Description = "The version/name of the release", IsRequired = true)]
    public string Version { get; init; } = string.Empty;
    
    [CommandOption("component", 'c', Description = "The component to install", IsRequired = false)]
    public string Component { get; init; } = string.Empty;
    
    [CommandOption("target-folder", 'f', Description = "The target folder to download the release to", IsRequired = true)]
    public string TargetFolder { get; init; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        if (string.IsNullOrWhiteSpace(RepositoryName)) throw new CommandException("You must specify a repository name.");
        if (string.IsNullOrWhiteSpace(Version)) throw new CommandException("You must specify either a version to install.");
        if (!string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(RepositoryName)) throw new CommandException("You must specify a repository name when providing a version.");
        if (string.IsNullOrWhiteSpace(TargetFolder)) throw new CommandException("You must specify a target directory.");
        
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory);

        ReleaseSummaryDto? release = null;
        
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
        
        release = releases.FirstOrDefault(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        if (release == null)
        {
            console.WriteLine($"Release with version '{Version}' not found in repository '{RepositoryName}'.");
            return;
        }
        
        // Ensure the target folder exists
        if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
        
        // Download the release package
        await console.Output.WriteLineAsync($"Downloading release '{release!.Version}' (ID: {release.Id}) to '{TargetFolder}'...");
        
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
    public Guid Id { get; init; }
    
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
public class ReleaseInstallCommand : TenantCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository for the release", IsRequired = true)]
    public string RepositoryName { get; init; } = string.Empty;
    
    [CommandOption("version", 'v', Description = "The version/name of the release", IsRequired = true)]
    public string Version { get; init; } = string.Empty;
    
    [CommandOption("component", 'c', Description = "The component to install", IsRequired = false)]
    public string Component { get; init; } = string.Empty;

    
    [CommandOption("target-folder", 'f', Description = "The target folder to install the release to", IsRequired = true)]
    public string TargetFolder { get; init; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        if (string.IsNullOrWhiteSpace(RepositoryName)) throw new CommandException("You must specify a repository name.");
        if (string.IsNullOrWhiteSpace(Version)) throw new CommandException("You must specify either a version to install.");
        if (!string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(RepositoryName)) throw new CommandException("You must specify a repository name when providing a version.");
        if (string.IsNullOrWhiteSpace(TargetFolder)) throw new CommandException("You must specify a target directory.");
        
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, console: console);

        ReleaseSummaryDto? release = null;
        
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
        
        release = releases.FirstOrDefault(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        if (release == null)
        {
            console.WriteLine($"Release with version '{Version}' not found in repository '{RepositoryName}'.");
            return;
        }
        
        // Ensure the target folder exists
        if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
        
        // Download the release package
        await console.Output.WriteLineAsync($"Downloading release '{release!.Version}' (ID: {release.Id}) to '{TargetFolder}'...");
        
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