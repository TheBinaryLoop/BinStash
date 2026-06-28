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

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace BinStash.Infrastructure.Storage.Indexing;

internal sealed class AsyncLruHandlerCache<TKey> : IAsyncDisposable where TKey : notnull
{
    private sealed class CacheEntry
    {
        public required TKey Key;
        public required IndexedPackFileHandler Handler;
        public required LinkedListNode<TKey> LruNode;
    }

    private readonly int _capacity;
    private readonly Func<TKey, IndexedPackFileHandler> _factory;

    private readonly ConcurrentDictionary<TKey, CacheEntry> _entries = new();
    private readonly LinkedList<TKey> _lru = [];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public AsyncLruHandlerCache(int capacity, Func<TKey, IndexedPackFileHandler> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async ValueTask<HandlerLease> AcquireAsync(TKey key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Fast path: existing entry
        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.Handler.TryAcquireLease())
            {
                await TouchAsync(existing, cancellationToken).ConfigureAwait(false);
                return new HandlerLease(existing.Handler);
            }

            // Rare race: entry is being disposed, fall through to slow path.
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            // Re-check under lock
            if (_entries.TryGetValue(key, out existing))
            {
                if (!existing.Handler.TryAcquireLease())
                    throw new ObjectDisposedException(nameof(IndexedPackFileHandler), "Handler was being evicted.");

                MoveToFront(existing);
                return new HandlerLease(existing.Handler);
            }

            var handler = _factory(key);

            if (!handler.TryAcquireLease())
            {
                handler.Dispose();
                throw new InvalidOperationException("Failed to acquire lease on newly created handler.");
            }

            var node = new LinkedListNode<TKey>(key);
            _lru.AddFirst(node);

            var entry = new CacheEntry
            {
                Key = key,
                Handler = handler,
                LruNode = node
            };

            if (!_entries.TryAdd(key, entry))
            {
                handler.ReleaseLease();
                handler.Dispose();
                throw new InvalidOperationException("Failed to add handler to cache.");
            }

            await EvictIfNeededUnderLockAsync().ConfigureAwait(false);

            return new HandlerLease(handler);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask TouchAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                MoveToFront(entry);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void MoveToFront(CacheEntry entry)
    {
        if (entry.LruNode.List is null)
            return;

        if (!ReferenceEquals(_lru.First, entry.LruNode))
        {
            _lru.Remove(entry.LruNode);
            _lru.AddFirst(entry.LruNode);
        }
    }

    private ValueTask EvictIfNeededUnderLockAsync()
    {
        while (_entries.Count > _capacity)
        {
            var node = _lru.Last;
            if (node is null)
                break;

            var key = node.Value;

            if (!_entries.TryGetValue(key, out var entry))
            {
                _lru.RemoveLast();
                continue;
            }

            // Do not evict active entries.
            if (!entry.Handler.IsIdle)
                break;

            if (!entry.Handler.TryMarkForDispose())
            {
                // Already being disposed or raced
                _lru.Remove(entry.LruNode);
                _entries.TryRemove(key, out _);
                continue;
            }

            _lru.Remove(entry.LruNode);
            _entries.TryRemove(key, out _);

            entry.Handler.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask TrimAsync(int targetCount, CancellationToken cancellationToken = default)
    {
        if (targetCount < 0)
            throw new ArgumentOutOfRangeException(nameof(targetCount));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (_entries.Count > targetCount)
            {
                var node = _lru.Last;
                if (node is null)
                    break;

                var key = node.Value;

                if (!_entries.TryGetValue(key, out var entry))
                {
                    _lru.RemoveLast();
                    continue;
                }

                if (!entry.Handler.IsIdle)
                    break;

                if (!entry.Handler.TryMarkForDispose())
                {
                    _lru.Remove(entry.LruNode);
                    _entries.TryRemove(key, out _);
                    continue;
                }

                _lru.Remove(entry.LruNode);
                _entries.TryRemove(key, out _);

                entry.Handler.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Handler.TryMarkForDispose();
                entry.Handler.Dispose();
            }

            _entries.Clear();
            _lru.Clear();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AsyncLruHandlerCache<>));
    }
}