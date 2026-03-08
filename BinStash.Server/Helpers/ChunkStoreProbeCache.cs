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

namespace BinStash.Server.Helpers;

public sealed record ChunkStoreProbeResult(
    Guid StoreId,
    string StoreName,
    string RootPath,
    string Status, // "Healthy" | "Degraded" | "Unhealthy"
    string? Error,
    long FreeBytes,
    long? TotalBytes,
    double WriteMs,
    double ReadMs,
    DateTimeOffset Timestamp
);

public sealed class ChunkStoreProbeCache
{
    private readonly Lock _gate = new();
    private Dictionary<Guid, ChunkStoreProbeResult> _results = new();
    private DateTimeOffset _updatedAt;

    public (IReadOnlyDictionary<Guid, ChunkStoreProbeResult> Results, DateTimeOffset UpdatedAt) Snapshot()
    {
        lock (_gate) return (_results, _updatedAt);
    }

    public void Update(IEnumerable<ChunkStoreProbeResult> results)
    {
        lock (_gate)
        {
            _results = results.ToDictionary(r => r.StoreId, r => r);
            _updatedAt = DateTimeOffset.UtcNow;
        }
    }
}