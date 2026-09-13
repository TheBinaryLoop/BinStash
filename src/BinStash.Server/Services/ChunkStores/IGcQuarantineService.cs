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
using BinStash.Core.Entities;

namespace BinStash.Server.Services.ChunkStores;

/// <summary>
/// Reverses garbage-collection quarantine for objects an ingest turns out to still need.
///
/// <para>
/// Quarantine deliberately deletes an object's <see cref="Chunk"/> / <see cref="FileDefinition"/>
/// row while leaving its bytes in the pack store. That makes the object invisible to the
/// "which of these do you already have?" path, so any ingest that starts afterwards simply
/// re-uploads it. The gap this service closes is the ingest that asked <em>before</em> the
/// quarantine: it was told the object was present, did not upload it, and would otherwise
/// commit a release pointing at an object the catalogue no longer knows about.
/// </para>
/// <para>
/// Because the bytes are still there, repairing that is a pure catalogue operation — restore
/// the row from the tombstone and drop the tombstone. It cannot fail for want of data, which is
/// what lets the collector quarantine aggressively without ever coordinating with writers.
/// </para>
/// </summary>
public interface IGcQuarantineService
{
    /// <summary>
    /// Lifts quarantine from <paramref name="hashes"/>: restores any catalogue row that is
    /// missing and removes the tombstones.
    ///
    /// <para>
    /// Changes are staged on the caller's <c>DbContext</c> and not saved, so a caller can make
    /// resurrection atomic with whatever else it is committing — notably the release row that
    /// depends on it.
    /// </para>
    /// </summary>
    /// <returns>The number of objects that were actually under quarantine.</returns>
    Task<int> ResurrectAsync(
        Guid chunkStoreId,
        GcObjectCategory category,
        IReadOnlyCollection<Hash32> hashes,
        CancellationToken ct = default);

    /// <summary>
    /// The subset of <paramref name="hashes"/> currently under quarantine. Lets a caller report
    /// on, or reason about, quarantine without changing it.
    /// </summary>
    Task<HashSet<Hash32>> GetQuarantinedAsync(
        Guid chunkStoreId,
        GcObjectCategory category,
        IReadOnlyCollection<Hash32> hashes,
        CancellationToken ct = default);
}
