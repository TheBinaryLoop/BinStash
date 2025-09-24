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

using BinStash.Contracts.Delta;
using BinStash.Contracts.Release;
using BinStash.Core.Types;
using DeltaChunkRef = BinStash.Contracts.Delta.DeltaChunkRef;

namespace BinStash.Server.Helpers;

public static class DeltaCalculator
{
    public static (DeltaManifest manifest, List<string> newChunkChecksums) ComputeChunkDeltaManifest(ReleasePackage oldRelease, ReleasePackage newRelease, string? singleComponent)
    {
        // Map: index → checksum
        var oldChunkMap = oldRelease.Chunks
            .Select((c, i) => new { Index = i, Checksum = Convert.ToHexStringLower(c.Checksum) })
            .ToDictionary(x => x.Index, x => x.Checksum);

        var newChunkMap = newRelease.Chunks
            .Select((c, i) => new { Index = i, Checksum = Convert.ToHexStringLower(c.Checksum) })
            .ToDictionary(x => x.Index, x => x.Checksum);

        var oldChecksumSet = oldChunkMap.Values.ToHashSet();
        var newChunks = new HashSet<string>();
        var files = new List<DeltaFile>();
        
        if (singleComponent != null)
        {
            // Filter components if single component is specified
            newRelease.Components = newRelease.Components
                .Where(c => c.Name.Equals(singleComponent, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var component in newRelease.Components)
        {
            foreach (var file in component.Files)
            {
                var chunkRefs = new List<DeltaChunkRef>();
                long totalSize = 0;

                foreach (var chunk in ChunkRefHelper.ConvertDeltaToChunkRefs(file.Chunks))
                {
                    var checksum = newChunkMap[chunk.Index];
                    var source = oldChecksumSet.Contains(checksum) ? "existing" : "new";

                    chunkRefs.Add(new DeltaChunkRef(
                        chunk.Index,
                        chunk.Offset,
                        checksum,
                        chunk.Length,
                        source
                    ));

                    totalSize += chunk.Length;

                    if (source == "new")
                        newChunks.Add(checksum);
                }
                
                // Get the old file hash from the old release if it exists
                var oldHash = oldRelease.Components.FirstOrDefault(x => x.Name == component.Name)?.Files.FirstOrDefault(x => x.Name == file.Name)?.Hash;

                files.Add(new DeltaFile(
                    (singleComponent != null ? file.Name : Path.Combine(component.Name, file.Name)).Replace('\\', '/'),
                    totalSize,
                    oldHash != null ? new Hash8(oldHash.Value).ToHexString() : string.Empty,
                    new Hash8(file.Hash).ToHexString(),
                    chunkRefs
                ));
            }
        }

        return (
            new DeltaManifest(oldRelease.ReleaseId, newRelease.ReleaseId, files.Where(x => x.Chunks.Any(chunkRef => chunkRef.Source == "new")).ToList()),
            newChunks.ToList()
        );
    }
}