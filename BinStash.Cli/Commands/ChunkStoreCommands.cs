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
using BinStash.Contracts.ChunkStore;
using BinStash.Core.Entities;
using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("chunk-store", Description = "Manage chunk stores")]
public class ChunkStoreRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'chunk-store'. Available subcommands: add, list, remove, show.", showHelp: true);
    }
}

[Command("chunk-store list", Description = "List all chunk store you have access to")]
public class ChunkStoreListCommand : AuthenticatedCommandBase
{
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl());
        var chunkStores = await client.GetChunkStoresAsync();
        if (chunkStores == null || chunkStores.Count == 0)
        {
            await console.Output.WriteLineAsync("No chunk stores found.");
            return;
        }
        await console.Output.WriteLineAsync("Available chunk stores:");
        foreach (var store in chunkStores)
        {
            await console.Output.WriteLineAsync($"- {store.Name} (ID: {store.Id})");
        }
    }
}

[Command("chunk-store add", Description = "Add a new chunk store")]
public class ChunkStoreAddCommand : AuthenticatedCommandBase
{
    [CommandOption("name", 'n', Description = "Name of the chunk store", IsRequired = true)]
    public string Name { get; init; } = string.Empty;
    
    [CommandOption("type", 't', Description = "Type of the chunk store (Local, S3)", IsRequired = true)]
    public ChunkStoreType ChunkStoreType { get; init; }

    [CommandOption("local-path", 'p', Description = "Local path for the chunk store (required for Local type)", IsRequired = false)]
    public string ChunkStoreLocalPath { get; init; } = string.Empty;
    
    [CommandOption("chunker-type", 'c', Description = "Type of the chunker (FastCdc)", IsRequired = false)]
    public ChunkerType? ChunkerType { get; set; }
    
    [CommandOption("min-chunk-size", 'm', Description = "Minimum chunk size in bytes", IsRequired = false)]
    public int ChunkerMinChunkSize { get; set; }
    
    [CommandOption("avg-chunk-size", 'a', Description = "Average chunk size in bytes", IsRequired = false)]
    public int ChunkerAvgChunkSize { get; set; }
    
    [CommandOption("max-chunk-size", 'x', Description = "Maximum chunk size in bytes", IsRequired = false)]
    public int ChunkerMaxChunkSize { get; set; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl());
        
        if (ChunkStoreType == ChunkStoreType.Local && string.IsNullOrWhiteSpace(ChunkStoreLocalPath))
        {
            throw new CommandException("Local path is required for Local chunk store type.");
        }
        
        var createChunkStoreDto = new CreateChunkStoreDto
        {
            Name = Name,
            Type = ChunkStoreType.ToString(),
            LocalPath = ChunkStoreLocalPath,
        };
        
        if (ChunkerType is not null)
        {
            createChunkStoreDto.Chunker = new ChunkStoreChunkerDto
            {
                Type = ChunkerType.Value.ToString(),
                MinChunkSize = ChunkerMinChunkSize > 0 ? ChunkerMinChunkSize : null,
                AvgChunkSize = ChunkerAvgChunkSize > 0 ? ChunkerAvgChunkSize : null,
                MaxChunkSize = ChunkerMaxChunkSize > 0 ? ChunkerMaxChunkSize : null
            };
        }
        
        var chunkStore = await client.CreateChunkStoreAsync(createChunkStoreDto);
        
        if (chunkStore == null)
        {
            await console.Output.WriteLineAsync("Failed to create chunk store. Please check the provided parameters.");
            return;
        }
        
        await console.Output.WriteLineAsync("Chunk Store Details:");
        await console.Output.WriteLineAsync($"- ID: {chunkStore.Id}");
        await console.Output.WriteLineAsync($"- Name: {chunkStore.Name}");
        await console.Output.WriteLineAsync($"- Type: {chunkStore.Type}");
        await console.Output.WriteLineAsync($"- Chunker: {chunkStore.Chunker.Type}");
        await console.Output.WriteLineAsync($"  - Min chunk size: {chunkStore.Chunker.MinChunkSize}");
        await console.Output.WriteLineAsync($"  - Avg chunk size: {chunkStore.Chunker.AvgChunkSize}");
        await console.Output.WriteLineAsync($"  - Max chunk size: {chunkStore.Chunker.MaxChunkSize}");
    }
}

[Command("chunk-store delete", Description = "Delete a chunk store")]
public class ChunkStoreDeleteCommand : AuthenticatedCommandBase
{
    [CommandOption("name", 'n', Description = "Name of the chunk store", IsRequired = true)]
    public string Name { get; init; } = string.Empty;

    protected override ValueTask ExecuteCommandAsync(IConsole console)
    {
        throw new NotImplementedException();
    }
}

[Command("chunk-store show", Description = "Display infos about a chunk store")]
public class ChunkStoreShowCommand : AuthenticatedCommandBase
{
    [CommandOption("id", 'i', Description = "Id of the chunk store", IsRequired = true)]
    public Guid Id { get; init; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl());
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
        await console.Output.WriteLineAsync($"- Stats:");
        foreach (var chunkStoreStat in chunkStore.Stats)
        {
            await console.Output.WriteLineAsync($"  - {chunkStoreStat.Key}: {chunkStoreStat.Value}");
        }
    }
}

/*[Command("chunk-store test", Description = "Dummy command to test the CLI")]
public class ChunkStoreTestCommand : ICommand
{
    [CommandOption("path", 'p', Description = "The path to be uploaded", IsRequired = true)]
    public string UploadPath { get; init; } = string.Empty;
    
    [CommandOption("component-map", 'c', Description = "The path to the component map file.", IsRequired = false)]
    public string ComponentMapFile { get; init; } = string.Empty;
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
    }
    
    public static IReadOnlyList<ChunkMapEntry> MergeChunkMaps(IEnumerable<IEnumerable<ChunkMapEntry>> chunkMaps)
    {
        var merged = chunkMaps.SelectMany(map => map).ToList();
        return merged;
    }

    public static IReadOnlyList<ChunkMapEntry> MergeChunkMapsDeduplicated(IEnumerable<IEnumerable<ChunkMapEntry>> chunkMaps)
    {
        return chunkMaps
            .SelectMany(map => map)
            .GroupBy(c => c.Checksum)
            .Select(g => g.First()) // or add custom priority logic
            .ToList();
    }

    
    private void PrintChunkSizeHistogram(IConsole console, IReadOnlyList<ChunkMapEntry> chunks)
    {
        console.WriteLine("\nChunk Size Histogram:");
        var buckets = new[]
        {
            (Label: "< 1 KiB", Max: 1 * 1024L),
            (Label: "1-4 KiB", Max: 4 * 1024L),
            (Label: "4-16 KiB", Max: 16 * 1024L),
            (Label: "16-64 KiB", Max: 64 * 1024L),
            (Label: "64-256 KiB", Max: 256 * 1024L),
            (Label: "256 KiB - 1 MiB", Max: 1024 * 1024L),
            (Label: "> 1 MiB", Max: long.MaxValue)
        };

        var sizes = chunks.Select(c => (long)c.Length);

        foreach (var (label, max) in buckets)
        {
            int count = sizes.Count(s => s <= max);
            console.WriteLine($"  {label,-18} : {count,5}");
            sizes = sizes.Where(s => s > max);
        }

        console.WriteLine();
    }
    
    private void PrintDeduplicationStats(IConsole console, IReadOnlyList<ChunkMapEntry> chunks)
    {
        long totalBytes = chunks.Sum(c => (long)c.Length);
        int totalChunks = chunks.Count;
        int uniqueChunks = chunks.Select(c => c.Checksum).Distinct().Count();
        long uniqueBytes = chunks
            .GroupBy(c => c.Checksum)
            .Select(g => (long)g.First().Length)
            .Sum();

        double dedupFactor = totalBytes / (double)uniqueBytes;

        console.WriteLine("Deduplication Statistics:");
        console.WriteLine($"  Total chunks       : {totalChunks:N0}");
        console.WriteLine($"  Unique chunks      : {uniqueChunks:N0}");
        console.WriteLine($"  Total data size    : {FormatSize(totalBytes)}");
        console.WriteLine($"  Unique data size   : {FormatSize(uniqueBytes)}");
        console.WriteLine($"  Deduplication ratio: {dedupFactor:N2}x\n");
    }
    
    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KiB", "MiB", "GiB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}*/