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

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Entities;
using BinStash.Core.Storage;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Server.Services.ChunkStores;

namespace BinStash.Server.Services.Releases;

/// <summary>
/// Recomputes the content metrics of a release from its stored package.
/// </summary>
/// <remarks>
/// These are derived values — everything needed is in the <c>.rdef</c> plus the file
/// definitions in the chunk store — which is what makes a backfill possible at all.
/// Builds before 2026-03 never recorded logical size or chunk count, so releases ingested
/// by them report 0 B and 0 chunks until this runs over them.
///
/// The arithmetic is deliberately identical to the ingest path (IngestSessionEndpoints),
/// which now calls the same helpers, so a backfilled release and a freshly ingested one
/// cannot drift apart.
/// </remarks>
public static class ReleaseMetricsCalculator
{
    /// <summary>Total size of the release as published, before dedup and compression.</summary>
    public static ulong CalculateLogicalBytes(ReleasePackage package)
    {
        ulong total = 0;
        foreach (var artifact in package.OutputArtifacts)
            total += CalculateLogicalArtifactSize(artifact);
        return total;
    }

    public static ulong CalculateLogicalArtifactSize(OutputArtifact artifact)
        => artifact.Backing switch
        {
            OpaqueBlobBacking opaque => opaque.Length.HasValue ? (ulong)opaque.Length.Value : 0UL,
            ReconstructedContainerBacking reconstructed => CalculateReconstructedArtifactSize(reconstructed),
            _ => 0UL
        };

    private static ulong CalculateReconstructedArtifactSize(ReconstructedContainerBacking backing)
    {
        ulong total = 0;
        foreach (var member in backing.Members)
        {
            if (member.Length.HasValue)
                total += (ulong)member.Length.Value;
        }
        return total;
    }

    /// <summary>
    /// Number of DISTINCT chunks the release references, resolved by reading the file
    /// definitions its artifacts point at. Chunks shared between files are counted once,
    /// matching how ingest reports it.
    /// </summary>
    public static async Task<int> CountUniqueChunksAsync(
        ReleasePackage package,
        ChunkStore store,
        IChunkStoreService chunkStoreService,
        CancellationToken ct = default)
    {
        var contentHashes = CollectContentHashes(package).Distinct().ToList();
        if (contentHashes.Count == 0)
            return 0;

        var definitions = await chunkStoreService.RetrieveFileDefinitionsAsync(
            store,
            contentHashes.Select(h => h.ToHexString()).ToArray());

        var unique = new HashSet<Hash32>();
        foreach (var (_, blob) in definitions)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var chunkHash in FileDefinitionRecord.Deserialize(blob).ChunkHashes)
                unique.Add(chunkHash);
        }

        return unique.Count;
    }

    private static IEnumerable<Hash32> CollectContentHashes(ReleasePackage package)
    {
        foreach (var artifact in package.OutputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque when opaque.ContentHash is not null:
                    yield return opaque.ContentHash.Value;
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash is not null)
                            yield return member.ContentHash.Value;
                    }
                    break;
            }
        }
    }
}
