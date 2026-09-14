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

namespace BinStash.Core.Storage.Gc;

/// <summary>
/// The reachable set built by a garbage-collection mark phase, partitioned by bucket and
/// spilled to disk.
///
/// <para>
/// A mark phase cannot hold its result in memory. A store is sized in objects, not in
/// bytes-the-server-has: at a billion chunks a single in-memory hash set would be tens of
/// gigabytes, and the mark phase would fail exactly on the stores that need collecting most.
/// Partitioning by the same three-character prefix the pack store already uses turns that into
/// 8192 append-only files, each small enough to load, deduplicate and compare on its own. The
/// sweep then works one bucket at a time with bounded memory, whatever the store's size.
/// </para>
/// <para>
/// Writes are buffered per bucket and flushed in batches. Duplicates are not filtered on the
/// way in — a chunk shared by many files is appended many times — because deduplicating at
/// load time is exact and cheap, whereas an approximate filter on the write path could drop a
/// mark, and a dropped mark is deleted data.
/// </para>
/// </summary>
public sealed class GcMarkSet : IDisposable
{
    /// <summary>Hashes buffered per bucket before the buffer is appended to its file.</summary>
    private const int BucketFlushThreshold = 4096;

    /// <summary>
    /// Total buffered hashes across all buckets that triggers a full flush, bounding peak
    /// memory at roughly 32 bytes times this value regardless of how the marks are spread.
    /// </summary>
    private const int GlobalFlushThreshold = 1 << 20;

    private readonly string _directory;
    private readonly Dictionary<string, List<Hash32>> _buffers = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private int _bufferedTotal;

    /// <summary>
    /// Set once the spill files are deleted. From that point the set is inert: there is nothing
    /// left to write to, and the run it belonged to has finished with it either way.
    /// </summary>
    private bool _discarded;

    public GcMarkSet(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>The directory holding this run's spill files.</summary>
    public string SpillDirectory => _directory;

    /// <summary>
    /// Records <paramref name="hash"/> as reachable. Safe to call from several marking tasks at
    /// once.
    /// </summary>
    public void Add(GcObjectCategory category, Hash32 hash)
    {
        var key = BucketKey(category, BucketPrefixOf(hash));

        List<Hash32>? toFlush = null;
        var flushAll = false;

        lock (_gate)
        {
            if (!_buffers.TryGetValue(key, out var buffer))
            {
                buffer = new List<Hash32>(BucketFlushThreshold);
                _buffers[key] = buffer;
            }

            buffer.Add(hash);
            _bufferedTotal++;

            if (buffer.Count >= BucketFlushThreshold)
            {
                toFlush = buffer;
                _buffers[key] = new List<Hash32>(BucketFlushThreshold);
                _bufferedTotal -= toFlush.Count;
            }
            else if (_bufferedTotal >= GlobalFlushThreshold)
            {
                flushAll = true;
            }
        }

        if (toFlush is not null)
            AppendToFile(key, toFlush);

        if (flushAll)
            Flush();
    }

    /// <summary>Writes every buffered hash to disk. Call before reading any bucket back.</summary>
    public void Flush()
    {
        List<KeyValuePair<string, List<Hash32>>> pending;

        lock (_gate)
        {
            // Once the spill directory is gone there is nowhere to flush to, and nothing worth
            // flushing: the run that owned these marks is over. Writing would recreate the
            // directory the run just deleted, and fail anyway if the parent is gone with it.
            if (_discarded)
                return;

            pending = _buffers.Where(static kvp => kvp.Value.Count > 0).ToList();
            foreach (var kvp in pending)
                _buffers[kvp.Key] = new List<Hash32>(BucketFlushThreshold);
            _bufferedTotal = 0;
        }

        foreach (var (key, buffer) in pending)
            AppendToFile(key, buffer);
    }

    /// <summary>
    /// Loads and deduplicates one bucket's marks. Returns an empty set when the bucket was
    /// never marked — which, for a bucket that holds data, is precisely the case where
    /// everything in it is garbage.
    /// </summary>
    public HashSet<Hash32> LoadBucket(GcBucketId bucket)
    {
        var path = FilePathFor(BucketKey(bucket.Category, bucket.Prefix));
        var result = new HashSet<Hash32>();

        if (!File.Exists(path))
            return result;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.SequentialScan);

        Span<byte> buf = stackalloc byte[Hash32.Size];
        while (true)
        {
            var read = fs.ReadAtLeast(buf, Hash32.Size, throwOnEndOfStream: false);
            if (read < Hash32.Size)
                break;

            result.Add(new Hash32(buf));
        }

        return result;
    }

    /// <summary>
    /// The prefixes of every bucket in <paramref name="category"/> that received at least one
    /// mark. Used to drive the second marking pass without revisiting empty buckets.
    /// </summary>
    public IReadOnlyList<string> MarkedPrefixes(GcObjectCategory category)
    {
        var prefix = $"{category}-";

        if (!System.IO.Directory.Exists(_directory))
            return [];

        return System.IO.Directory.EnumerateFiles(_directory, $"{prefix}*.marks")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrEmpty(name))
            .Select(name => name![prefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Removes the run's spill files. Safe to call more than once.</summary>
    public void DeleteSpillFiles()
    {
        lock (_gate)
            _discarded = true;

        try
        {
            if (System.IO.Directory.Exists(_directory))
                System.IO.Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Best effort: leftover spill files cost disk, never correctness, and are
            // reclaimed by the next run that reuses the directory.
        }
    }

    public void Dispose()
    {
        // A dispose that throws replaces whatever ended the run with an I/O error. That is how a
        // clean cancellation came to be logged as an unhandled fault: the run deletes its spill
        // files in a finally block, and the using-scope then disposed the set afterwards and
        // tried to flush into the directory that had just been removed.
        try
        {
            Flush();
        }
        catch
        {
            // Nothing downstream can use these marks: the only caller disposes at the end of a
            // run, by which point the set has either been consumed or abandoned.
        }
    }

    // -----------------------------------------------------------------------
    // Helpers

    /// <summary>
    /// The 4096 possible bucket prefixes, precomputed. Marking runs once per referenced chunk
    /// across the whole store, so formatting a prefix per call would dominate the phase.
    /// </summary>
    private static readonly string[] PrefixTable =
        Enumerable.Range(0, 4096).Select(static i => i.ToString("x3")).ToArray();

    /// <summary>
    /// The bucket prefix of a hash: its first three lowercase hex characters, derived straight
    /// from the leading bytes rather than by formatting the whole hash.
    /// </summary>
    private static string BucketPrefixOf(Hash32 hash)
    {
        Span<byte> bytes = stackalloc byte[Hash32.Size];
        hash.WriteBytes(bytes);
        return PrefixTable[(bytes[0] << 4) | (bytes[1] >> 4)];
    }

    private static string BucketKey(GcObjectCategory category, string prefix) => $"{category}-{prefix}";

    private string FilePathFor(string key) => Path.Combine(_directory, $"{key}.marks");

    private void AppendToFile(string key, List<Hash32> hashes)
    {
        if (hashes.Count == 0)
            return;

        var payload = new byte[hashes.Count * Hash32.Size];
        for (var i = 0; i < hashes.Count; i++)
            hashes[i].WriteBytes(payload.AsSpan(i * Hash32.Size, Hash32.Size));

        using var fs = new FileStream(FilePathFor(key), FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024);
        fs.Write(payload);
    }
}
