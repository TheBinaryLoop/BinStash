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

namespace BinStash.Server.Configuration;

/// <summary>
/// Periodic chunk-store statistics collection. Bind to the <c>ChunkStoreStats</c> section.
/// </summary>
public sealed class ChunkStoreStatsSettings
{
    public const string SectionName = "ChunkStoreStats";

    /// <summary>
    /// How often a snapshot is collected for every chunk store.
    /// </summary>
    /// <remarks>
    /// A run is expensive: the logical figures are derived by walking every release definition
    /// and resolving its hashes in batches, which on a store with a few thousand releases is
    /// minutes of continuous database work. The snapshots feed trend charts, so resolution
    /// past a few hours buys nothing that the chart can show.
    /// </remarks>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Whether a run may reuse the previous snapshot's logical figures when the store is
    /// provably unchanged.
    /// </summary>
    /// <remarks>
    /// Set to <c>false</c> to force a full recomputation every run, which is the way to repair
    /// a snapshot written from bad data — the reuse path would otherwise copy it forward.
    /// </remarks>
    public bool ReuseUnchangedLogicalStats { get; set; } = true;
}
