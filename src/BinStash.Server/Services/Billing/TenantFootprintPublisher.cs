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

using BinStash.Core.Billing;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;

namespace BinStash.Server.Services.Billing;

/// <summary>
/// Records a computed footprint and hands the billable figure across the billing boundary.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="TenantFootprintCalculator"/> so that the expensive, read-only
/// walk can be exercised — and tested — without writing anything or notifying a billing plugin.
/// </remarks>
public interface ITenantFootprintPublisher
{
    Task PublishAsync(TenantStorageSnapshot snapshot, CancellationToken ct = default);
}

/// <inheritdoc cref="ITenantFootprintPublisher"/>
public sealed class TenantFootprintPublisher : ITenantFootprintPublisher
{
    private readonly BinStashDbContext _db;
    private readonly IUsageMeteringService _metering;
    private readonly ILogger<TenantFootprintPublisher> _logger;

    public TenantFootprintPublisher(BinStashDbContext db, IUsageMeteringService metering, ILogger<TenantFootprintPublisher> logger)
    {
        _db = db;
        _metering = metering;
        _logger = logger;
    }

    public async Task PublishAsync(TenantStorageSnapshot snapshot, CancellationToken ct = default)
    {
        _db.TenantStorageSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        // Only the deduplicated figure crosses the boundary. The plugin decides plans and limits
        // from it; it never sees what the shared store physically holds, which is not this
        // tenant's to be charged for or told about.
        await _metering.RecordStorageSnapshotAsync(snapshot.TenantId, snapshot.UniqueLogicalBytes, ct);

        _logger.LogDebug(
            "Billing: tenant {TenantId} holds {UniqueBytes} unique bytes across {Chunks} chunks " +
            "({LogicalBytes} logical) — walked in {Duration}",
            snapshot.TenantId, snapshot.UniqueLogicalBytes, snapshot.UniqueChunkCount,
            snapshot.LogicalBytes, snapshot.Duration);
    }
}
