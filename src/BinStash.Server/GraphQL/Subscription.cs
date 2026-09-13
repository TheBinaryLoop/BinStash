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

using BinStash.Server.Services.ReleaseUpgrade;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;

namespace BinStash.Server.GraphQL;

public sealed class Subscription
{
    /// <summary>
    /// Streams progress updates for a single background job (chunk-store rebuild or release upgrade).
    /// Backed by the in-memory topic "BackgroundJobProgress_{jobId}" published by the background services.
    /// </summary>
    [Subscribe(With = nameof(SubscribeToBackgroundJobProgressAsync))]
    public BackgroundJobProgressDto BackgroundJobProgress(Guid jobId, [EventMessage] BackgroundJobProgressDto message)
        => message;

    public ValueTask<ISourceStream<BackgroundJobProgressDto>> SubscribeToBackgroundJobProgressAsync(
        Guid jobId,
        [Service] ITopicEventReceiver receiver,
        CancellationToken cancellationToken)
        => receiver.SubscribeAsync<BackgroundJobProgressDto>($"BackgroundJobProgress_{jobId}", cancellationToken);
}
