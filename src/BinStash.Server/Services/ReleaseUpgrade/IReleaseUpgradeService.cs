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

namespace BinStash.Server.Services.ReleaseUpgrade;

/// <summary>
/// Orchestrates the safe, per-release upgrade of .rdef files to the latest serializer version.
/// Fixes BUG-04 (no deletion of current-version files), ERR-03 (atomic write-before-delete),
/// and PERF-05 (batched SaveChanges).
/// </summary>
public interface IReleaseUpgradeService
{
    /// <summary>
    /// Executes the upgrade job identified by <paramref name="jobId"/>.
    /// Progress is persisted to the database and broadcast via GraphQL subscriptions.
    /// </summary>
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
