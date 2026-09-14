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

using System.Threading.Channels;

namespace BinStash.Server.Services.Billing;

/// <summary>
/// Tenants whose footprint should be recomputed ahead of the next scheduled sweep.
///
/// <para>
/// The scheduled sweep runs daily, which would let a tenant publish a day of releases before the
/// figure their plan limit is checked against moved at all. Queueing the tenant when an ingest
/// finalizes closes that window to the length of one walk without maintaining any incremental
/// accounting: the footprint is still only ever established by a full walk, just a more timely one.
/// </para>
///
/// <para>
/// Requests collapse. A tenant already queued is not queued again, so a build matrix pushing ten
/// targets in a minute causes one recomputation, not ten.
/// </para>
/// </summary>
public sealed class TenantFootprintRefreshQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    private readonly HashSet<Guid> _pending = [];
    private readonly Lock _gate = new();

    public void Enqueue(Guid tenantId)
    {
        lock (_gate)
        {
            if (!_pending.Add(tenantId))
                return;
        }

        _channel.Writer.TryWrite(tenantId);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// Marks a tenant as no longer queued. Called once the walk has started reading, so an ingest
    /// that lands mid-walk queues the tenant again rather than being folded into a result that
    /// may already have passed its releases.
    /// </summary>
    public void Release(Guid tenantId)
    {
        lock (_gate)
            _pending.Remove(tenantId);
    }
}
