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

using System.Collections.Concurrent;
using BinStash.Contracts.Hashing;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;
using Blake3;

namespace BinStash.Core.Ingestion.Execution;

public sealed class ContentProcessor : IContentProcessor
{
    public FileHashingResult HashFiles(IngestionResult ingestionResult, int degreeOfParallelism = 0)
    {
        if (ingestionResult.FileBindings.Count == 0)
        {
            return new FileHashingResult(
                FileHashes: new Dictionary<Hash32, IReadOnlyList<string>>(),
                FileSizes: new Dictionary<Hash32, long>(),
                FileBindings: []);
        }

        degreeOfParallelism = degreeOfParallelism <= 0
            ? Environment.ProcessorCount
            : degreeOfParallelism;

        var fileHashes = new ConcurrentDictionary<Hash32, ConcurrentBag<string>>();
        var fileSizes = new ConcurrentDictionary<Hash32, long>();

        Parallel.ForEach(ingestionResult.FileBindings, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, binding =>
            {
                var input = binding.Input ?? throw new InvalidOperationException("ReleaseFileBinding.Input must be set.");
                
                using var stream = OpenInputStream(input);
                
                var hasher = Hasher.New();
                Span<byte> buffer = stackalloc byte[65536];
                long totalBytesRead = 0;

                int bytesRead;
                while ((bytesRead = stream.Read(buffer)) > 0)
                {
                    hasher.Update(buffer[..bytesRead]);
                    totalBytesRead += bytesRead;
                }

                var fileHashBytes = hasher.Finalize();
                var fileHash = new Hash32(fileHashBytes.AsSpan());

                binding.File.Hash = fileHash;

                var key = BuildBindingIdentity(input);
                fileHashes.GetOrAdd(fileHash, _ => new ConcurrentBag<string>()) .Add(key);

                fileSizes[fileHash] = totalBytesRead;
            });

        var frozenHashes = fileHashes.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value.ToList());

        return new FileHashingResult(
            FileHashes: frozenHashes,
            FileSizes: new Dictionary<Hash32, long>(fileSizes),
            FileBindings: ingestionResult.FileBindings);
    }

    public ChunkMapGenerationResult GenerateChunkMaps(FileHashingResult hashingResult, IngestionResult ingestionResult, IChunker chunker, IReadOnlySet<Hash32> missingFileChecksums, int degreeOfParallelism = 0)
    {
        if (hashingResult.FileBindings.Count == 0 || missingFileChecksums.Count == 0)
        {
            return new ChunkMapGenerationResult(
                FileChunkMaps: new Dictionary<Hash32, List<ChunkMapEntry>>());
        }

        degreeOfParallelism = degreeOfParallelism <= 0
            ? Environment.ProcessorCount
            : degreeOfParallelism;

        var fileChunkMaps = new ConcurrentDictionary<Hash32, List<ChunkMapEntry>>();
        var missingEntries = hashingResult.FileBindings
            .Where(x => missingFileChecksums.Contains(x.File.Hash))
            .ToList();

        Parallel.ForEach(missingEntries, new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism }, binding =>
        {
            var input = binding.Input ?? throw new InvalidOperationException("ReleaseFileBinding.Input must be set.");

            var chunkMap = GenerateChunkMap(input, chunker).ToList();
            fileChunkMaps.TryAdd(binding.File.Hash, chunkMap);
        });

        foreach (var kvp in fileChunkMaps)
        {
            if (ingestionResult.Contents.TryGetValue(kvp.Key, out var content))
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

    private static IEnumerable<ChunkMapEntry> GenerateChunkMap(InputItem input, IChunker chunker)
    {
        if (input.Kind == InputItemKind.FileSystemFile)
            return chunker.GenerateChunkMap(input.AbsolutePath);

        if (input.OpenRead == null)
            throw new InvalidOperationException("Container entry input requires OpenRead.");

        var locator = BuildChunkSourceLocator(input);

        return chunker.GenerateChunkMap(input.OpenRead())
            .Select(x => new ChunkMapEntry
            {
                FilePath = locator,
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            })
            .ToList();
    }

    private static string BuildChunkSourceLocator(InputItem input)
    {
        if (input.Kind == InputItemKind.FileSystemFile)
            return input.AbsolutePath;

        if (string.IsNullOrWhiteSpace(input.EntryPath))
            throw new InvalidOperationException("Container entry input requires EntryPath.");

        // Format: zip|<outer-zip-path>|<entry-path>
        return $"zip|{input.AbsolutePath}|{input.EntryPath}";
    }
}