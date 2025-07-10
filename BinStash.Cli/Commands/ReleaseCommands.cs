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
using System.IO.Hashing;
using BinStash.Cli.Infrastructure;
using BinStash.Contracts.Release;
using BinStash.Core.Chunking;
using BinStash.Core.Entities;
using BinStash.Core.Serialization;
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
public class ReleasesListCommand : AuthenticatedCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository to query", IsRequired = true)]
    public string RepositoryName { get; set; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl());
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
        await console.Output.WriteLineAsync("Available releases:");
        foreach (var release in releases)
        {
            await console.Output.WriteLineAsync($"- {release.Version} created at {release.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} (ID: {release.Id})");
        }
    }
}

[Command("release add", Description = "Add a new release")]
public class ReleasesAddCommand : AuthenticatedCommandBase
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
            Components = new(),
            Chunks = new(),
            Stats = new()
        };
        var fileEntries = new ConcurrentBag<(ReleaseFile File, List<ChunkMapEntry> Entries, Component Component)>();
        var allChunkChecksums = new ConcurrentBag<string>();
        var chunkMaps = new ConcurrentBag<List<ChunkMapEntry>>();
        
        var client = new BinStashApiClient(GetUrl());
        
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
                
                ctx.Status($"Fetching chunk store for repository '{repository.Name}'...");
                
                var chunkStore = await client.GetChunkStoreAsync(repository.ChunkStoreId);
                if (chunkStore == null)
                {
                    ansiConsole.WriteLine($"Chunk store for repository '{repository.Name}' not found.");
                    return;
                }
                
                WriteLogMessage(ansiConsole, $"Repository [purple]{repository.Name}[/] uses chunk store: [bold blue]{chunkStore.Name}[/] (ID: [bold blue]{chunkStore.Id}[/])");
                
                ctx.Status("Setting up chunker ...");
                var chunker = (Enum.TryParse<ChunkerType>(chunkStore.Chunker.Type, true, out var chunkerType) ? chunkerType : ChunkerType.FastCdc) switch
                {
                    ChunkerType.FastCdc => new FastCdcChunker(chunkStore.Chunker.MinChunkSize!.Value, chunkStore.Chunker.AvgChunkSize!.Value, chunkStore.Chunker.MaxChunkSize!.Value),
                    _ => throw new NotSupportedException($"Unsupported chunker type: {chunkStore.Chunker.Type}")
                };
                
                WriteLogMessage(ansiConsole, $"Using chunker: [purple]{chunker.GetType().Name}[/] with min size [bold blue]{chunkStore.Chunker.MinChunkSize}[/], avg size [bold blue]{chunkStore.Chunker.AvgChunkSize}[/], max size [bold blue]{chunkStore.Chunker.MaxChunkSize}[/]");
                
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

                        var chunkMap = chunker.GenerateChunkMap(file).ToList();
                        if (chunkMap.Count == 0)
                        {
                            WriteLogMessage(ansiConsole, $"No chunks generated for file: [red]{file}[/]");
                            return;
                        }
                        
                        using var fsIn = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan); // 64 KB buffer
                        var hasher = new XxHash3();
                        Span<byte> buffer = stackalloc byte[65536]; // 64 KB, fits L1/L2 cache well, maybe need to adjust based on the system
                        int bytesRead;
                        while ((bytesRead = fsIn.Read(buffer)) > 0)
                        {
                            hasher.Append(buffer[..bytesRead]);
                        }
                        var fileHash = hasher.GetCurrentHash();
                        var releaseFile = new ReleaseFile
                        {
                            Name = file.Replace(RootFolder, string.Empty).Replace(componentMapEntry.Key, string.Empty).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            Hash = fileHash,
                            Chunks = new()
                        };

                        fileEntries.Add((releaseFile, chunkMap, componentMapEntry.Value));
                        chunkMaps.Add(chunkMap);

                        foreach (var entry in chunkMap)
                        {
                            allChunkChecksums.Add(entry.Checksum);
                        }
                        
                        // TODO: Implement log levels and print this on debug level only
                        //WriteLogMessage(ansiConsole, $"Processed file [bold blue]{releaseFile.Component}[/]:[purple]{releaseFile.Name}[/](Chunks: [bold blue]{chunkMap.Count}[/])");
#if xDEBUG
                    }
#else
                    });
#endif
                }
                
                WriteLogMessage(ansiConsole, $"Processed [bold blue]{chunkMaps.Sum(x => x.Count)}[/] chunks from [bold blue]{chunkMaps.Count}[/] files from the specified folder in [darkorange]{sw.Elapsed}[/]");
                
                ctx.Status($"Building chunk map for release definition with {chunkMaps.Sum(x => x.Count)} chunks...");
                if (chunkMaps.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "No chunks found in the specified folder. Please check the folder path and try again.");
                    return;
                }
                
                var uniqueChecksums = allChunkChecksums.Distinct().OrderBy(x => x).ToList();
                var checksumToIndex = uniqueChecksums.Select((checksum, index) => new { checksum, index })
                    .ToDictionary(x => x.checksum, x => (uint)x.index);

                foreach (var group in fileEntries.GroupBy(x => x.Component))
                {
                    var component = group.Key;
                    foreach (var (file, entries, _) in group)
                    {
                        var lastIndex = 0u;
                        var first = true;

                        file.Chunks = entries.OrderBy(e => checksumToIndex[e.Checksum]).Select(entry =>
                        {
                            var currentIndex = checksumToIndex[entry.Checksum];
                            uint deltaIndex;

                            if (first)
                            {
                                deltaIndex = currentIndex;
                                first = false;
                            }
                            else
                            {
                                deltaIndex = currentIndex - lastIndex;
                            }

                            lastIndex = currentIndex;

                            return new DeltaChunkRef(deltaIndex, (ulong)entry.Offset, (ulong)entry.Length);
                        }).ToList();


                        component.Files.Add(file);
                    }
                }

                releasePackage.Chunks = uniqueChecksums.Select((checksum) => new ChunkInfo(Convert.FromHexString(checksum))).ToList();
                
                releasePackage.Stats = new ReleaseStats
                {
                    FileCount = (uint)releasePackage.Components.SelectMany(c => c.Files).Count(),
                    ChunkCount = (uint)releasePackage.Chunks.Count,
                    UncompressedSize = (ulong)chunkMaps.SelectMany(x => x).Sum(x => (long)x.Length),
                    CompressedSize = (ulong)releasePackage.Components.SelectMany(x => x.Files.SelectMany(f => f.Chunks)).Sum(x => (long)x.Length) // fake estimate
                };
                
                WriteLogMessage(ansiConsole, $"Release definition contains [bold blue]{uniqueChecksums.Count}[/] unique chunks");
                
                ctx.Status("Requesting missing chunk info from chunk store...");
                var missingChunks = await client.GetMissingChunkChecksumAsync(chunkStore.Id, uniqueChecksums);
                if (missingChunks.Count == 0)
                {
                    WriteLogMessage(ansiConsole, "No missing chunks found in the chunk store. All chunks are already available");
                }
                else
                {
                    WriteLogMessage(ansiConsole, $"Found [bold blue]{missingChunks.Count}[/] missing chunks that need to be uploaded to the chunk store");
                    
                    ctx.Status("Calculating chunk map entries for upload...");
                    sw.Restart();
                    var missingSet = new HashSet<string>(missingChunks, StringComparer.Ordinal);
                    var selectedEntries = new ConcurrentDictionary<string, ChunkMapEntry>(StringComparer.Ordinal);

                    Parallel.ForEach(chunkMaps, chunkMap =>
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
                    WriteLogMessage(ansiConsole, $"Calculated [bold blue]{missingChunkEntries.Count}[/] chunk map entries for upload in {sw.Elapsed}");
                    sw.Stop();
                    
                    ctx.Status("Uploading missing chunks to chunk store...");
                    await client.UploadChunksAsync(chunker, chunkStore.Id, missingChunkEntries, progressCallback: (uploaded, total) => Task.Run(() => ctx.Status($"Uploading missing chunks to chunk store ({uploaded}/{total} ({(double)uploaded / total:P2}))...")));
                }
                
                WriteLogMessage(ansiConsole, "All missing chunks uploaded to the chunk store");
                
                ctx.Status("Uploading release package...");
                await client.CreateReleaseAsync(repository.Id.ToString(), releasePackage);
                
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
public class ReleaseDeleteCommand : AuthenticatedCommandBase
{
    [CommandOption("id", 'i', Description = "Id of the release", IsRequired = true)]
    public Guid ReleaseId { get; init; }

    protected override ValueTask ExecuteCommandAsync(IConsole console)
    {
        throw new NotImplementedException();
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
public class ReleaseDownloadCommand : AuthenticatedCommandBase
{
    [CommandOption("repository", 'r', Description = "Repository for the release", IsRequired = false)]
    public string RepositoryName { get; init; } = string.Empty;
    
    [CommandOption("release-id", 'i', Description = "The id of the release", IsRequired = false)]
    public Guid? ReleaseId { get; init; }
    
    [CommandOption("version", 'v', Description = "The version/name of the release", IsRequired = false)]
    public string Version { get; init; } = string.Empty;
    
    [CommandOption("target-folder", 't', Description = "The target folder to install the release to", IsRequired = true)]
    public string TargetFolder { get; init; } = string.Empty;

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        if (string.IsNullOrWhiteSpace(Version) && !ReleaseId.HasValue)
        {
            throw new CommandException("You must specify either a release ID or a version to download.");
        }
        
        if (string.IsNullOrWhiteSpace(Version) && !ReleaseId.HasValue) throw new CommandException("You must specify either a version or a release ID to install.");
        if (!string.IsNullOrEmpty(Version) && string.IsNullOrEmpty(RepositoryName)) throw new CommandException("You must specify a repository name when providing a version.");
        if (string.IsNullOrWhiteSpace(TargetFolder)) throw new CommandException("You must specify a target directory.");
        
        var client = new BinStashApiClient(GetUrl());

        ReleaseSummaryDto? release = null;
        
        // If ReleaseId is specified, fetch the release directly
        if (ReleaseId != null)
        {
            var releases = await client.GetReleasesAsync();
            if (releases == null || releases.Count == 0)
            {
                console.WriteLine("No releases found. Please create a release first.");
                return;
            }
            
            release = releases.FirstOrDefault(r => r.Id == ReleaseId.Value);
            if (release == null)
            {
                console.WriteLine($"Release with ID '{ReleaseId}' not found. Available releases:");
                foreach (var rel in releases)
                {
                    console.WriteLine($"- {rel.Version} (ID: {rel.Id})");
                }
                return;
            }
        }
        // If Version is specified, fetch the release by version and repository
        else if (!string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(RepositoryName))
        {
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
        }
        
        // Ensure the target folder exists
        if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
        
        // Download the release package
        await console.Output.WriteLineAsync($"Downloading release '{release!.Version}' (ID: {release.Id}) to '{TargetFolder}'...");
        
        var downloadPath = Path.Combine(TargetFolder, $"{release.Version}.tar.zst");

        if (!await client.DownloadReleaseAsync(release.Repository.Id, release.Id, downloadPath))
        {
            throw new CommandException($"Failed to download release '{release.Version}' (ID: {release.Id}) from repository '{release.Repository.Name}'.");
        }
    }
}