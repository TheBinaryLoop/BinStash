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

using System.Buffers;
using System.Collections.Concurrent;
using BinStash.Contracts.Hashing;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;
using Blake3;

namespace BinStash.Core.Ingestion.Execution;

public sealed class ContentProcessor : IContentProcessor
{
    public StorageHashingResult HashStorageWorkItems(IngestionResult ingestionResult, int degreeOfParallelism = 0)
    {
        if (ingestionResult.StorageWorkItems.Count == 0)
        {
            return new StorageHashingResult(
                ContentHashes: new Dictionary<Hash32, IReadOnlyList<string>>(),
                ContentSizes: new Dictionary<Hash32, long>(),
                WorkItemHashes: new Dictionary<string, Hash32>(StringComparer.OrdinalIgnoreCase),
                ItemResults: [],
                WorkItems: []);
        }

        degreeOfParallelism = degreeOfParallelism <= 0
            ? Environment.ProcessorCount
            : degreeOfParallelism;

        var contentHashes = new ConcurrentDictionary<Hash32, ConcurrentBag<string>>();
        var contentSizes = new ConcurrentDictionary<Hash32, long>();
        var workItemHashes = new ConcurrentDictionary<string, Hash32>(StringComparer.OrdinalIgnoreCase);
        var itemResults = new ConcurrentBag<StorageHashingItemResult>();
        
        Parallel.ForEach(ingestionResult.StorageWorkItems, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, workItem =>
        {
            byte[]? rented = null;
            try
            {
                using var stream = workItem.OpenRead();

                var hasher = Hasher.New();
                rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
                long totalBytesRead = 0;

                int bytesRead;
                while ((bytesRead = stream.Read(rented, 0, rented.Length)) > 0)
                {
                    hasher.Update(rented.AsSpan(0, bytesRead));
                    totalBytesRead += bytesRead;
                }

                var hash = new Hash32(hasher.Finalize().AsSpan());

                contentHashes.GetOrAdd(hash, _ => new ConcurrentBag<string>())
                    .Add(workItem.Identity);

                contentSizes[hash] = totalBytesRead;
                workItemHashes[workItem.Identity] = hash;

                itemResults.Add(new StorageHashingItemResult(
                    WorkItemIdentity: workItem.Identity,
                    Hash: hash,
                    Length: totalBytesRead));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed hashing storage work item '{workItem.Identity}' " +
                    $"(kind={workItem.Kind}, source='{workItem.SourcePath}', entry='{workItem.EntryPath}').",
                    ex);
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        });

        var frozenHashes = contentHashes.ToDictionary(x => x.Key, IReadOnlyList<string> (x) => x.Value.ToList());

        return new StorageHashingResult(
            ContentHashes: frozenHashes,
            ContentSizes: new Dictionary<Hash32, long>(contentSizes),
            WorkItemHashes: new Dictionary<string, Hash32>(workItemHashes, StringComparer.OrdinalIgnoreCase),
            ItemResults: itemResults.ToList(),
            WorkItems: ingestionResult.StorageWorkItems);
    }

    public ChunkMapGenerationResult GenerateChunkMaps(StorageHashingResult hashingResult, IngestionResult ingestionResult, IChunker chunker, IReadOnlySet<Hash32> missingContentHashes, int degreeOfParallelism = 0)
    {
        if (hashingResult.WorkItems.Count == 0 || missingContentHashes.Count == 0)
        {
            return new ChunkMapGenerationResult(
                FileChunkMaps: new Dictionary<Hash32, List<ChunkMapEntry>>());
        }

        degreeOfParallelism = degreeOfParallelism <= 0
            ? Environment.ProcessorCount
            : degreeOfParallelism;

        var fileChunkMaps = new ConcurrentDictionary<Hash32, List<ChunkMapEntry>>();
        
        Parallel.ForEach(hashingResult.WorkItems, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, workItem =>
        {
            if (!hashingResult.WorkItemHashes.TryGetValue(workItem.Identity, out var hash))
                return;

            if (!missingContentHashes.Contains(hash))
                return;

            var chunkMap = GenerateChunkMap(workItem, chunker).ToList();
            fileChunkMaps.TryAdd(hash, chunkMap);
        });

        foreach (var kvp in fileChunkMaps)
        {
            if (ingestionResult.StoredContents.TryGetValue(kvp.Key, out var content))
            {
                content.ChunkMap = kvp.Value;
            }
        }

        return new ChunkMapGenerationResult(
            FileChunkMaps: new Dictionary<Hash32, List<ChunkMapEntry>>(fileChunkMaps));
    }
    
    private static Stream OpenInputStream(InputItem input)
    {
        if (input.Kind == InputItemKind.ContainerEntry)
        {
            if (input.OpenRead == null)
                throw new InvalidOperationException("Container entry input requires OpenRead.");

            return input.OpenRead();
        }

        return new FileStream(input.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
    }

    private static string BuildBindingIdentity(InputItem input)
    {
        return input.Kind == InputItemKind.ContainerEntry
            ? $"{input.AbsolutePath}!/{input.EntryPath}"
            : input.AbsolutePath;
    }

    private static IEnumerable<ChunkMapEntry> GenerateChunkMap(StorageWorkItem workItem, IChunker chunker)
    {
        if (workItem.Kind == StorageWorkItemKind.OpaqueFile && !string.IsNullOrWhiteSpace(workItem.SourcePath))
            return chunker.GenerateChunkMap(workItem.SourcePath);

        using var stream = workItem.OpenRead();
        var chunkMap = chunker.GenerateChunkMap(stream).ToList();

        if (workItem.Kind == StorageWorkItemKind.ExtractedContainerMember)
        {
            return chunkMap.Select(x => new ChunkMapEntry
            {
                FilePath = BuildChunkSourceLocator(workItem),
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            }).ToList();
        }

        return chunkMap;
    }

    private static string BuildChunkSourceLocator(StorageWorkItem workItem)
    {
        if (workItem.Kind == StorageWorkItemKind.OpaqueFile)
            return workItem.SourcePath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workItem.SourcePath) || string.IsNullOrWhiteSpace(workItem.EntryPath))
            throw new InvalidOperationException("Extracted container member work item requires SourcePath and EntryPath.");

        return $"zip|{workItem.SourcePath}|{workItem.EntryPath}";
    }
}