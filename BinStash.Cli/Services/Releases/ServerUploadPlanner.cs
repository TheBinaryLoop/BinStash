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

using BinStash.Cli.Infrastructure;
using BinStash.Contracts.Hashing;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Cli.Services.Releases;

public sealed class ServerUploadPlanner
{
    public async Task<ServerUploadPlan> CreateAsync(BinStashApiClient client, Guid repositoryId, Guid ingestSessionId, StorageHashingResult hashingResult, ChunkMapGenerationResult chunkMapResult, CancellationToken ct = default)
    {
        var uniqueChunkChecksums = chunkMapResult.FileChunkMaps.Values
            .SelectMany(x => x.Select(cme => cme.Checksum))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var missingChunkChecksums = uniqueChunkChecksums.Count == 0 ? [] : await client.GetMissingChunkChecksumsAsync(repositoryId, ingestSessionId, uniqueChunkChecksums);

        var missingChunkSet = new HashSet<Hash32>(missingChunkChecksums);
        var selectedEntries = new Dictionary<Hash32, ChunkMapEntry>();

        foreach (var chunkMap in chunkMapResult.FileChunkMaps.Values)
        {
            foreach (var entry in chunkMap)
            {
                if (missingChunkSet.Contains(entry.Checksum))
                {
                    selectedEntries.TryAdd(entry.Checksum, entry);
                }
            }
        }

        var fileDefinitions = chunkMapResult.FileChunkMaps.ToDictionary(
            x => x.Key,
            x => (
                Chunks: x.Value.Select(v => v.Checksum).ToList(),
                Length: hashingResult.ContentSizes[x.Key]
            ));

        return new ServerUploadPlan(
            MissingChunkChecksums: missingChunkChecksums,
            MissingChunkEntries: selectedEntries.Values.ToList(),
            FileDefinitions: fileDefinitions);
    }
}

public sealed record ServerUploadPlan(
    IReadOnlyList<Hash32> MissingChunkChecksums,
    IReadOnlyList<ChunkMapEntry> MissingChunkEntries,
    IReadOnlyDictionary<Hash32, (List<Hash32> Chunks, long Length)> FileDefinitions);