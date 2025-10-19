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
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Entities;
using DeltaChunkRef = BinStash.Contracts.Delta.DeltaChunkRef;

namespace BinStash.Server.Helpers;

public static class DeltaCalculator
{
    public static (DeltaManifest manifest, List<Hash32> newChunkChecksums, List<Hash32> newFileChecksums) ComputeDeltaManifest(List<(string Component, ReleaseFile File)> oldFiles, List<(string Component, ReleaseFile File)> newFiles, Dictionary<Hash32, List<(Hash32 Hash, int Length)>> fileChunkMap, List<Chunk> chunkInfos)
    {
        // Fast lookup for lengths if a tuple carries Length = -1
        var lengthByChecksum = chunkInfos
            .GroupBy(c => c.Checksum)
            .ToDictionary(g => g.Key, g => g.First().Length);

        static int ResolveLen(Hash32 hash, int provided, Dictionary<Hash32, int> lengthByChecksum)
            => provided >= 0 ? provided : (lengthByChecksum.GetValueOrDefault(hash, 0));

        var newChunks = new List<Hash32>();
        var newFileChecksums = new List<Hash32>();
        var singleComponentRequested = newFiles.Select(f => f.Component).Distinct().Count() == 1;
        var files = new List<DeltaFile>();

        foreach (var entry in newFiles)
        {
            var relPath = (singleComponentRequested ? entry.File.Name : Path.Combine(entry.Component, entry.File.Name)).Replace('\\', '/');

            // Look up matching old file (same component + name)
            var oldEntry = oldFiles.FirstOrDefault(x => x.Component == entry.Component && x.File.Name == entry.File.Name);

            // No old file: entirely new
            if (oldEntry is (null, null))
            {
                var sizeNew = fileChunkMap[entry.File.Hash].Sum(t => (long)ResolveLen(t.Hash, t.Length, lengthByChecksum));
                files.Add(new DeltaFile(relPath, sizeNew, /*oldHash*/ null, entry.File.Hash, "new", new List<DeltaChunkRef>()));
                newFileChecksums.Add(entry.File.Hash);
                continue;
            }

            // Unchanged file: keep
            if (entry.File.Hash == oldEntry.File!.Hash)
            {
                var sizeKeep = fileChunkMap[entry.File.Hash].Sum(t => (long)ResolveLen(t.Hash, t.Length, lengthByChecksum));
                files.Add(new DeltaFile(relPath, sizeKeep, oldEntry.File.Hash, entry.File.Hash, "keep", new List<DeltaChunkRef>()));
                continue;
            }

            // Changed file: compute delta with duplicate support
            var oldList = fileChunkMap[oldEntry.File.Hash];          // List<(Hash, Len)>
            var newList = fileChunkMap[entry.File.Hash];

            // Build a multiset (bag) of old chunks: Hash32 -> count
            var oldCounts = new Dictionary<Hash32, int>();
            foreach (var (h, _) in oldList)
                oldCounts[h] = oldCounts.TryGetValue(h, out var c) ? c + 1 : 1;

            var chunkRefs = new List<DeltaChunkRef>(newList.Count);
            long totalSize = 0;
            var i = 0;

            foreach (var (hash, lenProvided) in newList)
            {
                var len = ResolveLen(hash, lenProvided, lengthByChecksum);

                string source;
                if (oldCounts.TryGetValue(hash, out var cnt) && cnt > 0)
                {
                    source = "existing";
                    oldCounts[hash] = cnt - 1; // consume one instance
                }
                else
                {
                    source = "new";
                    newChunks.Add(hash);
                }

                chunkRefs.Add(new DeltaChunkRef(
                    /*index*/ i,
                    /*offset*/ totalSize,
                    /*checksum*/ hash,
                    /*length*/ len,
                    /*source*/ source
                ));

                totalSize += len;
                i++;
            }

            files.Add(new DeltaFile(
                relPath,
                totalSize,
                oldEntry.File.Hash,
                entry.File.Hash,
                "modified",
                chunkRefs
            ));
        }

        // Most callers want unique checksums to fetch; dedupe here.
        var uniqueNew = newChunks.Distinct().ToList();
        var uniqueNewFileChecksums = newFileChecksums.Distinct().ToList();
        return (new DeltaManifest(string.Empty, string.Empty, files), uniqueNew, uniqueNewFileChecksums);
    }

}