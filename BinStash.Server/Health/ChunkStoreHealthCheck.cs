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

using BinStash.Server.Helpers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BinStash.Server.Health;

public sealed class ChunkStoreHealthCheck : IHealthCheck
{
    private readonly ChunkStoreProbeCache _cache;

    public ChunkStoreHealthCheck(ChunkStoreProbeCache cache) => _cache = cache;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var (results, updatedAt) = _cache.Snapshot();

        // stale snapshot => degraded
        var age = DateTimeOffset.UtcNow - updatedAt;
        var stale = age > TimeSpan.FromSeconds(30);

        var anyUnhealthy = results.Values.Any(r => r.Status == "Unhealthy");
        var anyDegraded  = results.Values.Any(r => r.Status == "Degraded");

        var status =
            anyUnhealthy ? HealthStatus.Unhealthy :
            (anyDegraded || stale) ? HealthStatus.Degraded :
            HealthStatus.Healthy;

        var data = new Dictionary<string, object>
        {
            ["updatedAtUtc"] = updatedAt,
            ["ageSeconds"] = age.TotalSeconds,
            ["stale"] = stale,
            ["stores"] = results.Values.Select(r => new
            {
                r.StoreId,
                r.StoreName,
                r.RootPath,
                r.Status,
                r.Error,
                r.FreeBytes,
                r.TotalBytes,
                r.WriteMs,
                r.ReadMs,
                r.Timestamp
            }).ToArray()
        };

        return Task.FromResult(new HealthCheckResult(status, description: "Chunk store probe", data: data));
    }
}