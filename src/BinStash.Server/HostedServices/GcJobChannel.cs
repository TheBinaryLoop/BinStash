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

namespace BinStash.Server.HostedServices;

/// <summary>
/// Typed wrapper around a <see cref="Channel{Guid}"/> dedicated to chunk-store garbage-collection
/// jobs. Registered as its own singleton so GC work queues independently of the rebuild and
/// release-upgrade pipelines rather than competing with them for one worker.
/// </summary>
public sealed class GcJobChannel
{
    /// <summary>
    /// The underlying unbounded channel. Use <see cref="Channel.Writer"/> to enqueue job IDs and
    /// <see cref="Channel.Reader"/> to drain them in the background service.
    /// </summary>
    public Channel<Guid> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
}
