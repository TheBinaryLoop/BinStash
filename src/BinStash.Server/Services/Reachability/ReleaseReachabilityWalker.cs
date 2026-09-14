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
using BinStash.Core.Serialization;
using BinStash.Core.Storage;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Storage.FileDefinition;

namespace BinStash.Server.Services.Reachability;

/// <summary>
/// A release definition that roots a reachability walk.
/// </summary>
/// <remarks>
/// <see cref="Version"/> carries no meaning for the walk itself; it exists so that a failure can
/// name the release an operator would recognise rather than only its id.
/// </remarks>
public readonly record struct ReleaseRoot(Guid Id, string Version, Hash32 DefinitionChecksum);

/// <summary>
/// Raised when a walk cannot establish the full set of objects a release reaches.
///
/// <para>
/// The message states only the fact — what was unreadable and for which release. Callers append
/// the consequence, because it differs: garbage collection must refuse to collect, whereas usage
/// accounting must refuse to publish a snapshot. Both refuse; neither may proceed on a partial
/// answer, since a partial answer is indistinguishable from a small one.
/// </para>
/// </summary>
public sealed class ReachabilityAbortedException : Exception
{
    public ReachabilityAbortedException(string message) : base(message) { }
}

/// <summary>
/// Walks release definitions to the file definitions and chunks they reach.
///
/// <para>
/// Two consumers need this traversal and must agree on it exactly: garbage collection, which
/// treats anything it does not reach as collectable, and per-tenant usage accounting, which
/// bills a tenant for the distinct chunks their own releases reach. A discrepancy between the
/// two would either bill for collected bytes or collect billed ones, so the traversal lives
/// here once rather than twice.
/// </para>
/// </summary>
public sealed class ReleaseReachabilityWalker
{
    private readonly IChunkStoreStorage _storage;
    private readonly IChunkStoreGarbageCollector _collector;

    public ReleaseReachabilityWalker(IChunkStoreStorage storage, IChunkStoreGarbageCollector collector)
    {
        _storage = storage;
        _collector = collector;
    }

    /// <summary>
    /// Every file-content hash a release reaches, by reading and decoding its release package.
    /// </summary>
    /// <exception cref="ReachabilityAbortedException">
    /// The package is missing, unreadable, undecodable, or in a format whose file references
    /// cannot be resolved against the current file-definition index.
    /// </exception>
    public async Task<IReadOnlyList<Hash32>> ReadReferencedFileHashesAsync(
        ReleaseRoot release,
        CancellationToken ct = default)
    {
        var packageHash = release.DefinitionChecksum.ToHexString();

        byte[]? packageBytes;
        try
        {
            packageBytes = await _storage.RetrieveReleasePackageAsync(packageHash);
        }
        catch (Exception ex)
        {
            throw new ReachabilityAbortedException(
                $"Release '{release.Version}' ({release.Id}) references release package {packageHash}, " +
                $"which could not be read ({ex.GetType().Name}: {ex.Message}).");
        }

        if (packageBytes is null || packageBytes.Length == 0)
        {
            throw new ReachabilityAbortedException(
                $"Release '{release.Version}' ({release.Id}) references release package {packageHash}, " +
                "which is missing from the store.");
        }

        ReleasePackage package;
        try
        {
            (package, _) = await ReleasePackageSerializer.DeserializeAsync(packageBytes, ct);
        }
        catch (Exception ex)
        {
            throw new ReachabilityAbortedException(
                $"Release package {packageHash} for release '{release.Version}' ({release.Id}) could not be " +
                $"deserialized ({ex.GetType().Name}: {ex.Message}).");
        }

        if (package.PackageFormatVersion == 5)
        {
            // V5 addresses file definitions by StorageKey, an identity the store no longer
            // carries. Walking it would look successful and reach nothing.
            throw new ReachabilityAbortedException(
                $"Release '{release.Version}' ({release.Id}) is stored in the legacy V5 format, whose file " +
                "references cannot be resolved against the current file-definition index. Run the release " +
                "upgrade job on this chunk store first.");
        }

        return CollectReferencedFileHashes(package, release);
    }

    /// <summary>
    /// Every chunk hash a file definition reaches.
    /// </summary>
    /// <exception cref="ReachabilityAbortedException">
    /// The file definition is referenced by a release but missing from the store, or cannot be decoded.
    /// </exception>
    public async Task<IReadOnlyList<Hash32>> ReadChunkHashesAsync(Hash32 fileHash, CancellationToken ct = default)
    {
        var blob = await _collector.ReadFileDefinitionForMarkAsync(fileHash, ct);
        if (blob is null)
        {
            // Reachable from a release but not present. The release is already broken; saying so
            // is more useful than quietly treating its chunks as absent.
            throw new ReachabilityAbortedException(
                $"File definition {fileHash.ToHexString()} is referenced by a release but is missing from " +
                "the store. Rebuild the chunk store index, or repair the affected release.");
        }

        FileDefinitionRecord record;
        try
        {
            record = FileDefinitionRecord.Deserialize(blob);
        }
        catch (Exception ex)
        {
            throw new ReachabilityAbortedException(
                $"File definition {fileHash.ToHexString()} could not be decoded " +
                $"({ex.GetType().Name}: {ex.Message}).");
        }

        return record.ChunkHashes;
    }

    /// <summary>
    /// Every file-content hash a release package reaches, across both artifact backings.
    /// A backing the serializer does not recognise aborts the walk rather than contributing
    /// nothing, because "no hashes" and "no hashes I understood" are indistinguishable to a
    /// caller and only one of them is safe.
    /// </summary>
    public static IReadOnlyList<Hash32> CollectReferencedFileHashes(ReleasePackage package, ReleaseRoot release)
    {
        var hashes = new List<Hash32>();

        foreach (var artifact in package.OutputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    if (opaque.ContentHash is null)
                        throw new ReachabilityAbortedException(
                            $"Output artifact '{artifact.Path}' of release '{release.Version}' ({release.Id}) has no content hash.");
                    hashes.Add(opaque.ContentHash.Value);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash is null)
                            throw new ReachabilityAbortedException(
                                $"Container member '{member.EntryPath}' of '{artifact.Path}' in release " +
                                $"'{release.Version}' ({release.Id}) has no content hash.");
                        hashes.Add(member.ContentHash.Value);
                    }
                    break;

                default:
                    throw new ReachabilityAbortedException(
                        $"Output artifact '{artifact.Path}' of release '{release.Version}' ({release.Id}) uses backing type " +
                        $"'{artifact.Backing.GetType().Name}', which this server does not know how to walk.");
            }
        }

        return hashes;
    }
}
