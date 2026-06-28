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
using BinStash.Contracts.ChunkStore;
using BinStash.Core.Entities;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace BinStash.Cli.Commands;

[Command("chunk-store", Description = "Manage chunk stores")]
public partial class ChunkStoreRootCommand : ICommand
{
    public ValueTask ExecuteAsync(IConsole console)
    {
        throw new CommandException("Please specify a subcommand for 'chunk-store'. Available subcommands: add, list, remove, show.", showHelp: true);
    }
}

[Command("chunk-store list", Description = "List all chunk store you have access to")]
public partial class ChunkStoreListCommand : AuthenticatedCommandBase
{
    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, authScheme: AuthScheme);
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
public partial class ChunkStoreAddCommand : AuthenticatedCommandBase
{
    [CommandOption("name", 'n', Description = "Name of the chunk store")]
    public required string Name { get; set; } = string.Empty;
    
    [CommandOption("type", 't', Description = "Type of the chunk store (Local, S3)")]
    public required ChunkStoreType ChunkStoreType { get; set; }

    [CommandOption("local-path", 'p', Description = "Local path for the chunk store (required for Local type)")]
    public string ChunkStoreLocalPath { get; set; } = string.Empty;
    
    [CommandOption("chunker-type", 'c', Description = "Type of the chunker (FastCdc)")]
    public ChunkerType? ChunkerType { get; set; }
    
    [CommandOption("min-chunk-size", 'm', Description = "Minimum chunk size in bytes")]
    public int ChunkerMinChunkSize { get; set; }
    
    [CommandOption("avg-chunk-size", 'a', Description = "Average chunk size in bytes")]
    public int ChunkerAvgChunkSize { get; set; }
    
    [CommandOption("max-chunk-size", 'x', Description = "Maximum chunk size in bytes")]
    public int ChunkerMaxChunkSize { get; set; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, authScheme: AuthScheme);
        
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
public partial class ChunkStoreDeleteCommand : AuthenticatedCommandBase
{
    [CommandOption("name", 'n', Description = "Name of the chunk store")]
    public required string Name { get; set; } = string.Empty;

    protected override ValueTask ExecuteCommandAsync(IConsole console)
    {
        throw new NotImplementedException();
    }
}

[Command("chunk-store show", Description = "Display infos about a chunk store")]
public partial class ChunkStoreShowCommand : AuthenticatedCommandBase
{
    [CommandOption("id", 'i', Description = "Id of the chunk store")]
    public required Guid Id { get; set; }

    protected override async ValueTask ExecuteCommandAsync(IConsole console)
    {
        var client = new BinStashApiClient(GetUrl(), AuthTokenFactory, authScheme: AuthScheme);
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

