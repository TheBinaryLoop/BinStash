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

using System.Collections.Concurrent;
using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;
using BinStash.Core.Storage.Gc;
using BinStash.Infrastructure.Storage.Packing;

namespace BinStash.Infrastructure.Storage.Indexing;

/// <summary>
/// Per-prefix chunk/file-definition pack-file handler with a three-tier,
/// LSM-tree-inspired index.
///
/// <para>
/// <strong>Tier 0 — append log (<c>index.log</c>):</strong>
/// New entries are appended to a binary log file using the same varint-encoded
/// format as the old monolithic <c>.idx</c> file.  All log entries are also
/// kept in the hot <c>_logDict</c> dictionary for O(1) in-memory deduplication.
/// When the log reaches <see cref="LogFlushThreshold"/> entries it is flushed
/// to an immutable sorted segment and the log file is truncated.
/// </para>
///
/// <para>
/// <strong>Tier 1+ — sorted segments (<c>seg-NNN.idx</c>):</strong>
/// Immutable fixed-width sorted files enabling O(log n) binary search via
/// memory-mapped I/O with zero heap allocation on the hot path.  Each segment
/// has a paired bloom filter (<c>seg-NNN.bloom</c>) for fast probabilistic
/// membership testing before the binary search.
/// </para>
///
/// <para>
/// <strong>Compaction:</strong>
/// After every log flush the handler checks whether any compaction is
/// warranted (size-tiered: 16 level-N segments → 1 level-(N+1) segment) and
/// runs the merge under the write lock.
/// </para>
///
/// <para>
/// <strong>Graceful degradation at small scale:</strong>
/// For deployments with fewer than <see cref="LogFlushThreshold"/> chunks per
/// prefix bucket the log never flushes, no segment files are created, and
/// behavior is identical to the original implementation.
/// </para>
/// </summary>
internal sealed class IndexedPackFileHandler : IDisposable
{
    // -----------------------------------------------------------------------
    // Tunables

    /// <summary>
    /// Number of log entries that trigger a flush to a sorted segment.
    /// At 1 B total chunks / 4096 buckets ≈ 244 K chunks/bucket, this means
    /// ~60 flushes per bucket at steady state.
    /// </summary>
    private const int LogFlushThreshold = 4096;

    private const int CompactionFanIn = 16; // 16 level-N segs → 1 level-(N+1) seg

    /// <summary>
    /// How long a replaced <see cref="SortedIndexSegment"/> is kept mapped after it has been
    /// swapped out of <see cref="_segments"/>.
    ///
    /// <para>
    /// Reads resolve against a lock-free snapshot of the segment list, so a reader can still be
    /// inside <see cref="SortedIndexSegment.TryFind"/> on a segment the write path has already
    /// replaced. Disposing that segment immediately unmaps the view underneath the reader.
    /// The segment's <em>file</em> may be deleted at once (both platforms keep an open mapping
    /// alive across unlink); only the managed disposal has to wait. A lookup takes microseconds,
    /// so a minute is many orders of magnitude of headroom.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SegmentDisposalGrace = TimeSpan.FromSeconds(60);

    /// <summary>Filename suffix marking a pack file that copy-forward compaction has superseded.</summary>
    internal const string RetiredMarkerSuffix = ".retired";

    /// <summary>Filename suffix of a compaction output that has not been published yet.</summary>
    internal const string CompactionTempSuffix = ".packtmp";

    // -----------------------------------------------------------------------
    // Identityf

    private readonly long _maxPackFileSize;
    private readonly string _directory;
    private readonly string _logFilePath;
    private readonly string _dataFilePrefix;
    private readonly string _indexFilePrefix; // same as _dataFilePrefix — used as a name prefix for all index files
    private readonly Func<ReadOnlySpan<byte>, Hash32> _computeHash;

    // -----------------------------------------------------------------------
    // Initialization guard

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialIndexLoadDone;

    // -----------------------------------------------------------------------
    // Write serialization

    /// <summary>
    /// Serializes all write-path mutations: appends, log flush, compaction.
    /// Reads are entirely lock-free against the volatile snapshot fields.
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // -----------------------------------------------------------------------
    // Tier 0: hot dictionary + append log

    /// <summary>
    /// In-memory mirror of the current append log.  Lock-free reads.
    /// Written only under <see cref="_writeLock"/> or during initialization.
    /// </summary>
    private ConcurrentDictionary<Hash32, IndexEntry> _logDict = new();

    private int _logEntryCount;

    private FileStream? _logAppendStream;
    private BinaryWriter? _logWriter;

    // -----------------------------------------------------------------------
    // Tier 1+: sorted segments

    /// <summary>
    /// Segments ordered newest-first.  Volatile for lock-free reads.
    /// Written only under <see cref="_writeLock"/> or during initialization.
    /// </summary>
    private volatile SegmentList _segments = SegmentList.Empty;

    // -----------------------------------------------------------------------
    // Pack file append state

    private int _currentFileNumber = int.MinValue;
    private FileStream? _currentStream;
    private long _currentStreamLength;

    /// <summary>
    /// Highest pack file number this handler has handed out, or <see cref="int.MinValue"/> when
    /// no number has been allocated yet.
    ///
    /// <para>
    /// Both the append path (rollover at 4 GiB) and compaction need fresh pack numbers, and they
    /// run concurrently by design — compaction streams bytes without holding the write lock.
    /// Routing every allocation through <see cref="AllocatePackFileNumberUnderLock"/> and this
    /// single counter is what stops the two from ever picking the same number and interleaving
    /// their writes into one file.
    /// </para>
    /// </summary>
    private int _highestAllocatedFileNumber = int.MinValue;

    // -----------------------------------------------------------------------
    // Deferred segment disposal

    /// <summary>
    /// Segments swapped out of <see cref="_segments"/> that are not safe to unmap yet.
    /// Drained opportunistically; see <see cref="SegmentDisposalGrace"/>.
    /// </summary>
    private readonly ConcurrentQueue<(SortedIndexSegment Segment, long DueTicks)> _retiredSegments = new();

    // -----------------------------------------------------------------------
    // LRU cache integration

    // Combined state field: encodes both the active-lease count and the
    // dispose-requested flag in a single int so that TryMarkForDispose and
    // TryAcquireLease are mutually exclusive without a separate lock.
    //
    // Layout:
    //   int.MinValue  (0x80000000) = dispose sentinel; no leases may be acquired
    //   0 .. int.MaxValue           = number of active leases (dispose not requested)
    //
    // TryAcquireLease: spins until it can CAS _state from N → N+1 where N >= 0.
    // TryMarkForDispose: CAS _state from exactly 0 → int.MinValue (idle → disposed).
    // ReleaseLease: Interlocked.Decrement.
    // IsIdle: _state == 0.
    private int _state; // 0 = idle; >0 = active leases; int.MinValue = dispose requested
    private bool _disposed;

    // -----------------------------------------------------------------------
    // Constructor

    public IndexedPackFileHandler(string directoryPath, string dataFileName, string prefix, long maxPackFileSize, Func<ReadOnlySpan<byte>, Hash32> computeHash)
    {
        _maxPackFileSize = maxPackFileSize;
        _computeHash     = computeHash ?? throw new ArgumentNullException(nameof(computeHash));

        Directory.CreateDirectory(directoryPath);

        _directory       = directoryPath;
        _dataFilePrefix  = Path.Combine(directoryPath, $"{dataFileName}{prefix}");
        _indexFilePrefix = _dataFilePrefix; // segment/log files are named <dataFilePrefix>.seg-NNN.idx etc.
        _logFilePath     = _indexFilePrefix + ".log";
    }

    // -----------------------------------------------------------------------
    // Initialization

    private async Task EnsureIndexLoadedAsync()
    {
        if (_initialIndexLoadDone)
            return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialIndexLoadDone)
                return;

            _segments      = LoadExistingSegments();
            _logDict       = LoadLogDict();
            _logEntryCount = _logDict.Count;

            _initialIndexLoadDone = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private SegmentList LoadExistingSegments()
    {
        var segFilePattern = Path.GetFileName(_indexFilePrefix) + ".seg-*.idx";
        var segFiles = Directory.EnumerateFiles(_directory, segFilePattern)
            .OrderByDescending(static f => f, StringComparer.Ordinal) // newest-first
            .ToArray();

        if (segFiles.Length == 0)
            return SegmentList.Empty;

        var entries = new List<SegmentEntry>(segFiles.Length);
        foreach (var path in segFiles)
        {
            var bloomPath = Path.ChangeExtension(path, ".bloom");
            try
            {
                var segment = new SortedIndexSegment(path);

                PackIndexBloomFilter? bloom = null;
                if (File.Exists(bloomPath))
                    bloom = PackIndexBloomFilter.Deserialize(File.ReadAllBytes(bloomPath));

                entries.Add(new SegmentEntry(path, segment, bloom));
            }
            catch
            {
                // Corrupt segment — skip; will be rebuilt on next RebuildIndexFile()
            }
        }

        return new SegmentList(entries.ToArray());
    }

    private ConcurrentDictionary<Hash32, IndexEntry> LoadLogDict()
    {
        var map = new ConcurrentDictionary<Hash32, IndexEntry>();

        if (!File.Exists(_logFilePath))
            return map;

        var fi = new FileInfo(_logFilePath);
        if (fi.Length == 0)
            return map;

        try
        {
            using var fs     = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

            while (fs.Position < fs.Length)
            {
                var hashBytes = reader.ReadBytes(32);
                if (hashBytes.Length < 32)
                    break; // truncated record

                var fileNo = VarIntUtils.ReadVarInt<int>(reader);
                var offset = VarIntUtils.ReadVarInt<long>(reader);
                var length = VarIntUtils.ReadVarInt<int>(reader);

                map[new Hash32(hashBytes)] = new IndexEntry(fileNo, offset, length);
            }
        }
        catch
        {
            // Corrupt log — return partial; fixed on next flush/rebuild
        }

        return map;
    }

    // -----------------------------------------------------------------------
    // Append log I/O

    private void EnsureLogAppendStreamOpen()
    {
        if (_logAppendStream is not null && _logWriter is not null)
            return;

        _logAppendStream?.Dispose();
        _logWriter?.Dispose();

        _logAppendStream = new FileStream(
            _logFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous);

        _logWriter = new BinaryWriter(_logAppendStream, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    private void AppendLogEntry(Hash32 hash, int fileNo, long offset, int length)
    {
        EnsureLogAppendStreamOpen();

        _logWriter!.Write(hash.GetBytes());
        VarIntUtils.WriteVarInt(_logWriter, fileNo);
        VarIntUtils.WriteVarInt(_logWriter, offset);
        VarIntUtils.WriteVarInt(_logWriter, length);
        _logWriter.Flush();
        _logAppendStream!.Flush(flushToDisk: true);
    }

    private void CloseLogAppendStream()
    {
        _logWriter?.Dispose();
        _logWriter = null;
        _logAppendStream?.Dispose();
        _logAppendStream = null;
    }

    // -----------------------------------------------------------------------
    // Lookup (read path — lock-free)

    private bool TryFindInIndex(Hash32 hash, out IndexEntry entry)
    {
        // 1. Hot dictionary (log entries)
        if (_logDict.TryGetValue(hash, out entry))
            return true;

        // 2. Segments newest-first: bloom → binary search
        var segs = _segments; // volatile snapshot
        foreach (var seg in segs.Entries)
        {
            if (seg.Bloom is not null && !seg.Bloom.MightContain(hash))
                continue;

            var found = seg.Segment.TryFind(hash);
            if (found.HasValue)
            {
                entry = found.Value;
                return true;
            }
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Log flush → new segment (called under _writeLock)

    private async Task FlushLogToSegmentAsync(CancellationToken ct = default)
    {
        if (_logEntryCount == 0)
            return;

        var sorted = _logDict
            .OrderBy(static kvp => kvp.Key)
            .Select(static kvp => (kvp.Key, kvp.Value))
            .ToList();

        var segPath   = NextSegmentPath(0);
        var bloomPath = Path.ChangeExtension(segPath, ".bloom");

        var bloom = new PackIndexBloomFilter(sorted.Count);
        foreach (var (hash, _) in sorted)
            bloom.Add(hash);

        await FileAtomicHelper.WriteAtomicAsync(bloomPath, bloom.Serialize(), ct).ConfigureAwait(false);
        await SortedIndexSegment.WriteAsync(segPath, sorted, ct).ConfigureAwait(false);

        var newSegment = new SortedIndexSegment(segPath);
        _segments = _segments.Prepend(new SegmentEntry(segPath, newSegment, bloom));

        // Truncate log and reset in-memory state
        CloseLogAppendStream();
        await File.WriteAllBytesAsync(_logFilePath, Array.Empty<byte>(), ct).ConfigureAwait(false);
        _logDict       = new ConcurrentDictionary<Hash32, IndexEntry>();
        _logEntryCount = 0;
    }

    private string NextSegmentPath(int level)
    {
        var namePrefix = Path.GetFileName(_indexFilePrefix);
        var segPattern = $"{namePrefix}.seg-{level}??.idx";
        var highest = Directory.EnumerateFiles(_directory, segPattern)
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(name =>
            {
                // name = "chunks000.seg-0NN" — last 2 chars are sequence number
                var seq = name.Length >= 2 ? name[^2..] : "";
                return int.TryParse(seq, out var n) ? n : -1;
            })
            .DefaultIfEmpty(-1)
            .Max();

        return Path.Combine(_directory, $"{namePrefix}.seg-{level}{highest + 1:D2}.idx");
    }

    // -----------------------------------------------------------------------
    // Compaction (size-tiered, called under _writeLock)

    private async Task RunCompactionIfNeededAsync(CancellationToken ct = default)
    {
        // Check levels 0 and 1 (level 2 is the maximum)
        for (var level = 0; level <= 1; level++)
            await CompactLevelAsync(level, ct).ConfigureAwait(false);
    }

    private async Task CompactLevelAsync(int level, CancellationToken ct)
    {
        var allSegs = _segments.Entries;

        // Collect segments at this level (oldest = tail, since list is newest-first)
        var atLevel = allSegs
            .Where(e => SortedIndexSegment.GetLevel(Path.GetFileName(e.SegmentPath)) == level)
            .ToArray();

        if (atLevel.Length < CompactionFanIn)
            return;

        // Oldest CompactionFanIn segments (tail of newest-first list)
        var toMerge = atLevel[^CompactionFanIn..];

        // K-way merge
        var merged = MergeSegments(toMerge.Select(static e => e.Segment).ToArray());

        var targetLevel  = level + 1;
        var outSegPath   = NextSegmentPath(targetLevel);
        var outBloomPath = Path.ChangeExtension(outSegPath, ".bloom");

        var bloom = new PackIndexBloomFilter(Math.Max(merged.Count, 1));
        foreach (var (hash, _) in merged)
            bloom.Add(hash);

        await FileAtomicHelper.WriteAtomicAsync(outBloomPath, bloom.Serialize(), ct).ConfigureAwait(false);
        await SortedIndexSegment.WriteAsync(outSegPath, merged, ct).ConfigureAwait(false);

        var newSeg = new SortedIndexSegment(outSegPath);

        // Rebuild segment list: remove merged, prepend new
        var remaining = allSegs
            .Where(e => !toMerge.Contains(e))
            .Prepend(new SegmentEntry(outSegPath, newSeg, bloom))
            .ToArray();

        _segments = new SegmentList(remaining);

        // Close + delete old segment files
        foreach (var seg in toMerge)
        {
            RetireSegment(seg.Segment);
            TryDeleteFile(seg.SegmentPath);
            TryDeleteFile(Path.ChangeExtension(seg.SegmentPath, ".bloom"));
        }
    }

    private static List<(Hash32 Hash, IndexEntry Entry)> MergeSegments(SortedIndexSegment[] segments)
    {
        // Read all entries from all segments, sort, de-duplicate
        var all = segments
            .SelectMany(static seg => seg.ReadAllEntries())
            .OrderBy(static e => e.Hash)
            .ToList();

        var result = new List<(Hash32, IndexEntry)>(all.Count);
        Hash32? last = null;
        foreach (var (hash, entry) in all)
        {
            if (last.HasValue && last.Value == hash)
                continue;
            result.Add((hash, entry));
            last = hash;
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // Public write API

    /// <summary>
    /// Stores <paramref name="data"/> under <paramref name="hash"/>, deduplicating against the
    /// existing index.
    /// </summary>
    /// <returns>
    /// <c>WasNew</c> — whether bytes were actually appended, and <c>Length</c> — the physical
    /// size of the pack entry either way.
    ///
    /// <para>
    /// The length is reported even for a duplicate on purpose. Callers maintain a database
    /// catalogue alongside the pack store, and the two can legitimately disagree: a crash
    /// between the pack append and the catalogue insert, or a garbage-collection quarantine
    /// that dropped the catalogue row while deliberately leaving the bytes in place. If a
    /// duplicate reported nothing, the caller would have no size to write and the catalogue
    /// would stay permanently out of step with the store.
    /// </para>
    /// </returns>
    public async Task<(bool WasNew, int Length)> WriteIndexedDataAsync(Hash32 hash, ReadOnlyMemory<byte> data)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        // Fast-path: lock-free duplicate check
        if (TryFindInIndex(hash, out var existing))
            return (false, existing.Length);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check under write lock
            if (TryFindInIndex(hash, out existing))
                return (false, existing.Length);

            var dataStream = await GetWritableDataFileAsync().ConfigureAwait(false);
            var fileNo     = _currentFileNumber;

            var (offset, length) = await PackFileEntry.WriteAsync(dataStream, data).ConfigureAwait(false);
            dataStream.Flush(flushToDisk: true);
            _currentStreamLength += length;

            var entry = new IndexEntry(fileNo, offset, length);
            _logDict.TryAdd(hash, entry);
            AppendLogEntry(hash, fileNo, offset, length);
            _logEntryCount++;

            if (_logEntryCount >= LogFlushThreshold)
            {
                await FlushLogToSegmentAsync().ConfigureAwait(false);
                await RunCompactionIfNeededAsync().ConfigureAwait(false);
            }

            return (true, length);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Public read API

    public async Task<byte[]> ReadIndexedDataAsync(Hash32 hash)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        if (!TryFindInIndex(hash, out var entry))
            throw new KeyNotFoundException($"No data with index {hash.ToHexString()}.");

        var path = $"{_dataFilePrefix}-{entry.FileNo}.pack";

        await using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1,
            options: FileOptions.Asynchronous | FileOptions.RandomAccess);

        return await PackFileEntry.ReadAtAsync(fs.SafeFileHandle, entry.Offset).ConfigureAwait(false)
               ?? throw new InvalidDataException($"Failed to read data for index {hash.ToHexString()}.");
    }

    // -----------------------------------------------------------------------
    // Rebuild / maintenance

    /// <summary>
    /// The exception that caused the most recent <see cref="RebuildIndexFile"/>
    /// to return <c>false</c>, or <c>null</c> if it succeeded. A rebuild failure
    /// is otherwise indistinguishable from any other, which makes a store that
    /// needs migrating very hard to diagnose from the outside.
    /// </summary>
    public Exception? LastRebuildError { get; private set; }

    /// <summary>
    /// Scans all pack files for this prefix, rebuilds the index from scratch,
    /// and writes a single sorted segment (choosing the level based on entry count).
    /// </summary>
    public async Task<bool> RebuildIndexFile()
    {
        LastRebuildError = null;

        await EnsureIndexLoadedAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            // Retired packs hold only entries that compaction already copied forward;
            // re-indexing them would resurrect the garbage the copy-forward dropped.
            var dataFiles = EnumeratePackFileNumbers(includeRetired: false)
                .Order()
                .Select(PackFilePath)
                .ToArray();

            var rebuilt = new ConcurrentDictionary<Hash32, IndexEntry>();

            foreach (var dataFile in dataFiles)
            {
                await using var fs = new FileStream(
                    dataFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                var fileNo = ParsePackFileNumber(dataFile);
                if (fileNo < 0) continue;

                await foreach (var packEntry in PackFileEntry.ReadAllEntriesAsync(fs).ConfigureAwait(false))
                {
                    var hash = _computeHash(packEntry.Data);
                    rebuilt.TryAdd(hash, new IndexEntry(fileNo, packEntry.Offset, packEntry.Length));
                }
            }

            // Swap the freshly derived index in without ever leaving the bucket unindexed —
            // a rebuild runs against a live store, and a reader that lands in the gap would be
            // told a perfectly healthy object does not exist.
            await PublishIndexUnderLockAsync(
                rebuilt.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value),
                CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            LastRebuildError = ex;
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Rewrites all pack files to remove orphaned / corrupt entries, then
    /// calls <see cref="RebuildIndexFile"/>.
    /// </summary>
    public Task<bool> RebuildPackFilesAsync()
        => RebuildPackFilesAsync(shouldKeep: null);

    /// <summary>
    /// Rewrites all pack files, optionally filtering entries via <paramref name="shouldKeep"/>,
    /// then calls <see cref="RebuildIndexFile"/>.
    /// Entries for which <paramref name="shouldKeep"/> returns <c>false</c> are silently dropped.
    /// Pass <c>null</c> to keep all entries (equivalent to <see cref="RebuildPackFilesAsync()"/>).
    /// </summary>
    public async Task<bool> RebuildPackFilesAsync(Func<byte[], bool>? shouldKeep)
    {
        await EnsureIndexLoadedAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            var dataFiles = EnumeratePackFileNumbers(includeRetired: false)
                .Order()
                .Select(PackFilePath)
                .ToArray();

            foreach (var dataFile in dataFiles)
            {
                var tmpDataFile = dataFile + ".tmp";

                await using var fs = new FileStream(
                    dataFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                await using var tmpFs = new FileStream(
                    tmpDataFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    options: FileOptions.Asynchronous);

                await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(fs, ignoreChecks: true).ConfigureAwait(false))
                {
                    if (shouldKeep is null || shouldKeep(entry.Data))
                        await PackFileEntry.WriteAsync(tmpFs, entry.Data).ConfigureAwait(false);
                }

                await tmpFs.FlushAsync().ConfigureAwait(false);
                tmpFs.Flush(flushToDisk: true);
                fs.Close();
                tmpFs.Close();

                File.Delete(dataFile);
                File.Move(tmpDataFile, dataFile);
            }

            _currentStream?.Dispose();
            _currentStream       = null;
            _currentFileNumber   = int.MinValue;
            _currentStreamLength = 0;

            return await RebuildIndexFile().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Garbage collection primitives
    //
    // The invariant these rely on: a pack file is append-only and is never modified in
    // place. Position within a bucket is therefore a total order on write time, and the
    // bytes behind an address a reader already resolved cannot move or change under it.
    // Reclaim honours that by copying live entries into a *new* pack and retiring the old
    // one afterwards, never by rewriting it.

    /// <summary>
    /// Captures the bucket's current append position.
    ///
    /// <para>
    /// Everything at or beyond the returned position is written after this call, so it
    /// cannot have been part of the object graph a mark phase starting now will observe.
    /// Treating those entries as implicitly live is what lets the collector run against
    /// live ingest traffic without coordinating with writers.
    /// </para>
    /// </summary>
    public async Task<GcBucketWatermark> SnapshotWatermarkAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var numbers = EnumeratePackFileNumbers(includeRetired: false).ToArray();
            if (numbers.Length == 0)
                return GcBucketWatermark.Empty;

            var highest = numbers.Max();

            // The append stream's own length is authoritative while it is open; the file
            // length can lag behind buffered writes.
            var length = _currentFileNumber == highest && _currentStream is not null
                ? _currentStreamLength
                : new FileInfo(PackFilePath(highest)).Length;

            return new GcBucketWatermark(highest, length);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Materialises the bucket's entire index as a hash → location map, resolving shadowed
    /// entries exactly the way <see cref="TryFindInIndex"/> does so that the snapshot and the
    /// live read path can never disagree.
    /// </summary>
    public async Task<Dictionary<Hash32, IndexEntry>> SnapshotIndexAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        var log  = _logDict;      // volatile field read once
        var segs = _segments;     // volatile field read once

        var result = new Dictionary<Hash32, IndexEntry>(log.Count + 1024);

        foreach (var kvp in log)
            result[kvp.Key] = kvp.Value;

        foreach (var seg in segs.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // TryAdd, not indexer assignment: lookup order is log first, then segments
            // newest-first, and the first hit wins. Mirroring that here keeps the snapshot
            // faithful even when the same hash appears in more than one tier.
            foreach (var (hash, entry) in seg.Segment.ReadAllEntries())
                result.TryAdd(hash, entry);
        }

        return result;
    }

    /// <summary>
    /// Physically removes <paramref name="doomed"/> from the bucket and republishes the index
    /// without them.
    ///
    /// <para>
    /// Work is done per source pack file. A pack is only touched when it is sealed (not the
    /// one currently being appended to), not already retired, and dead enough to be worth the
    /// rewrite. Its surviving entries are streamed into a freshly allocated pack, the bucket
    /// index is rebuilt to point at the new locations, and only then is the source marked
    /// retired — it stays on disk and readable until
    /// <see cref="PurgeObsoletePacksAsync"/> confirms nothing references it any more.
    /// </para>
    /// <para>
    /// Objects in packs that were skipped are counted in
    /// <see cref="GcReclaimResult.DeferredObjects"/>; the caller must keep their tombstones.
    /// </para>
    /// </summary>
    public async Task<GcReclaimResult> ReclaimAsync(
        IReadOnlyCollection<GcObjectRef> doomed,
        GarbageCollectionOptions options,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);

        var result = new GcReclaimResult();
        if (doomed.Count == 0)
            return result;

        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        var doomedByPack = doomed
            .GroupBy(static d => d.FileNo)
            .ToDictionary(static g => g.Key, static g => g.ToList());

        // The pack being appended to right now is off-limits: rewriting it would race the
        // writer, and its entries would be re-added behind our back. Deferring costs a run.
        int appendFileNo;
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            appendFileNo = _currentFileNumber;
            if (appendFileNo == int.MinValue)
                appendFileNo = EnumeratePackFileNumbers(includeRetired: false).DefaultIfEmpty(int.MinValue).Max();
        }
        finally
        {
            _writeLock.Release();
        }

        var compactedPacks = 0;

        foreach (var (fileNo, deadEntries) in doomedByPack.OrderByDescending(static kv => kv.Value.Sum(static d => (long)d.Length)))
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(RetiredMarkerPath(fileNo)) || !File.Exists(PackFilePath(fileNo)))
            {
                result.DeferredObjects += deadEntries.Count;
                continue;
            }

            if (fileNo == appendFileNo)
            {
                // Rewriting the append target would race the writer, so this pass cannot reclaim
                // it. Sealing it is the escape: the pack stops being the append target, and the
                // ordinary path above collects it on a later run. Without this, a bucket that
                // never reached the 4 GiB rollover would defer the same objects forever.
                if (options.SealAppendPackForCompaction &&
                    IsWorthCompacting(fileNo, deadEntries, options) &&
                    await TrySealAppendPackAsync(fileNo, ct).ConfigureAwait(false))
                {
                    result.PacksSealed++;
                }

                result.DeferredObjects += deadEntries.Count;
                continue;
            }

            if (compactedPacks >= options.MaxPacksToCompactPerRun)
            {
                result.DeferredObjects += deadEntries.Count;
                continue;
            }

            if (!IsWorthCompacting(fileNo, deadEntries, options))
            {
                // Not worth the read+write amplification yet. The tombstones stay, and once
                // more of the same pack dies the ratio will carry it over the line.
                result.DeferredObjects += deadEntries.Count;
                continue;
            }

            var reclaimed = await CompactPackFileAsync(fileNo, deadEntries, result.Warnings, ct).ConfigureAwait(false);
            if (reclaimed is null)
            {
                result.DeferredObjects += deadEntries.Count;
                continue;
            }

            compactedPacks++;
            result.PacksCompacted += reclaimed.RewroteLiveEntries ? 1 : 0;
            result.PacksRetired   += reclaimed.RewroteLiveEntries ? 0 : 1;
            result.ReclaimedBytes += reclaimed.FreedBytes;
            result.ReclaimedHashes.AddRange(reclaimed.RemovedHashes);
            result.DeferredObjects += deadEntries.Count - reclaimed.RemovedHashes.Count;
        }

        return result;
    }

    /// <summary>
    /// Whether enough of <paramref name="fileNo"/> is dead to justify rewriting it.
    /// </summary>
    private bool IsWorthCompacting(int fileNo, List<GcObjectRef> deadEntries, GarbageCollectionOptions options)
    {
        var packLength = new FileInfo(PackFilePath(fileNo)).Length;
        if (packLength <= 0)
            return false;

        var deadBytes = deadEntries.Sum(static d => (long)d.Length);
        return (double)deadBytes / packLength >= options.MinimumPackGarbageRatio;
    }

    /// <summary>
    /// Closes the bucket's current pack and starts a new one, so the closed pack becomes an
    /// ordinary compaction candidate. Returns <see langword="false"/> when the pack is no longer
    /// the append target, in which case there is nothing to seal.
    /// </summary>
    /// <remarks>
    /// The seal has to survive a restart, or the next process would simply adopt the same pack
    /// again and the deferral would resume. It does, because opening the successor creates it on
    /// disk: <see cref="OpenCurrentWritableFileAsync"/> picks the highest non-retired pack, which
    /// is now the new one. No existing entry is read, moved or rewritten, so a crash anywhere in
    /// here leaves the bucket exactly as it was, minus at most one empty pack file.
    /// </remarks>
    private async Task<bool> TrySealAppendPackAsync(int fileNo, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Re-check under the lock: the append target may have rolled over on its own between
            // the unlocked read at the top of the pass and now.
            var current = _currentFileNumber != int.MinValue
                ? _currentFileNumber
                : EnumeratePackFileNumbers(includeRetired: false).DefaultIfEmpty(int.MinValue).Max();

            if (current != fileNo)
                return false;

            _currentStream?.Dispose();
            _currentStream = null;

            _currentFileNumber = AllocatePackFileNumberUnderLock();
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private sealed record PackCompactionOutcome(List<Hash32> RemovedHashes, long FreedBytes, bool RewroteLiveEntries);

    /// <summary>
    /// Copies the live entries of one pack file forward into a new pack, republishes the
    /// bucket index, and retires the source. Returns <see langword="null"/> when the pack
    /// could not be processed and should be retried by a later run.
    /// </summary>
    private async Task<PackCompactionOutcome?> CompactPackFileAsync(
        int fileNo, List<GcObjectRef> deadEntries, List<string> warnings, CancellationToken ct)
    {
        var sourcePath = PackFilePath(fileNo);
        var deadSet    = deadEntries.Select(static d => d.Hash).ToHashSet();
        var deadOffsets= deadEntries.Select(static d => d.Offset).ToHashSet();

        // Phase A — stream the source into a new pack, outside the write lock. This is the
        // expensive part and it must not block ingest. Nothing published yet, so a crash here
        // leaves only an unreferenced temp file.
        int outputFileNo;
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            outputFileNo = AllocatePackFileNumberUnderLock();
        }
        finally
        {
            _writeLock.Release();
        }

        var tempPath   = $"{_dataFilePrefix}-{outputFileNo}{CompactionTempSuffix}";
        var outputPath = PackFilePath(outputFileNo);
        var relocations = new List<(Hash32 Hash, IndexEntry Entry)>();
        long freedBytes = 0;

        try
        {
            await using (var source = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                             bufferSize: 256 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                             tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 256 * 1024,
                             options: FileOptions.Asynchronous))
            {
                // ignoreChecks: a corrupt entry must not abort the whole pass. Entries that
                // fail to parse are treated as garbage and dropped, which is the only way a
                // store with historic corruption can ever be cleaned up.
                await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(source, ignoreChecks: true, ct).ConfigureAwait(false))
                {
                    if (deadOffsets.Contains(entry.Offset))
                    {
                        freedBytes += entry.Length;
                        continue;
                    }

                    Hash32 hash;
                    try
                    {
                        hash = _computeHash(entry.Data);
                    }
                    catch
                    {
                        // Unparseable payload: not referenced by anything that can read it.
                        freedBytes += entry.Length;
                        continue;
                    }

                    var (newOffset, newLength) = await PackFileEntry.WriteAsync(target, entry.Data, ct).ConfigureAwait(false);
                    relocations.Add((hash, new IndexEntry(outputFileNo, newOffset, newLength)));
                }

                await target.FlushAsync(ct).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            TryDeleteFile(tempPath);
            warnings.Add($"could not rewrite {Path.GetFileName(sourcePath)} ({ex.GetType().Name}: {ex.Message}); its tombstones are kept for a later run");
            return null;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            throw;
        }

        // Phase B — publish. Short and under the write lock: rename the output into place,
        // rebuild the bucket index around the relocations, then retire the source.
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var index = await SnapshotIndexAsync(ct).ConfigureAwait(false);

            var removed = new List<Hash32>(deadSet.Count);
            foreach (var dead in deadEntries)
            {
                // Only drop the index entry if it still points at the bytes we removed. A
                // concurrent re-upload of the same content would have written a fresh entry
                // elsewhere, and that one must survive.
                if (index.TryGetValue(dead.Hash, out var current) &&
                    current.FileNo == dead.FileNo && current.Offset == dead.Offset)
                {
                    index.Remove(dead.Hash);
                    removed.Add(dead.Hash);
                }
            }

            foreach (var (hash, entry) in relocations)
            {
                // Same rule in the other direction: never move a hash whose live location has
                // already been superseded by something newer than this pack.
                if (index.TryGetValue(hash, out var current) && current.FileNo == fileNo)
                    index[hash] = entry;
                else if (!index.ContainsKey(hash))
                    index[hash] = entry;
            }

            if (relocations.Count > 0)
                File.Move(tempPath, outputPath, overwrite: true);
            else
                TryDeleteFile(tempPath);

            await PublishIndexUnderLockAsync(index, ct).ConfigureAwait(false);

            // Retiring after the index no longer references the pack means a crash at any
            // point above leaves a consistent store — at worst some space stays occupied.
            await File.WriteAllTextAsync(
                RetiredMarkerPath(fileNo),
                DateTimeOffset.UtcNow.ToString("O"),
                ct).ConfigureAwait(false);

            return new PackCompactionOutcome(removed, freedBytes, relocations.Count > 0);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Replaces the whole bucket index with a single sorted segment built from
    /// <paramref name="index"/>, and empties the append log.
    ///
    /// <para>
    /// Collapsing to one segment is what keeps relocation correct. Segment lookup order is
    /// positional, so an entry that moved could otherwise stay shadowed by a stale copy in an
    /// older tier. With exactly one segment and an empty log there is nowhere for a stale copy
    /// to hide, and because ordinary writes never overwrite an existing hash, no duplicate can
    /// reappear afterwards.
    /// </para>
    /// </summary>
    private async Task PublishIndexUnderLockAsync(Dictionary<Hash32, IndexEntry> index, CancellationToken ct)
    {
        var sorted = index
            .OrderBy(static kvp => kvp.Key)
            .Select(static kvp => (kvp.Key, kvp.Value))
            .ToList();

        // Build the replacement completely before anything live is touched. Reads resolve
        // against these fields without taking a lock, so the old index has to stay whole and
        // usable right up to the instant the new one takes over — tearing it down first opens a
        // window in which a perfectly healthy object appears not to exist.
        SegmentList replacement;

        if (sorted.Count == 0)
        {
            replacement = SegmentList.Empty;
        }
        else
        {
            var level = sorted.Count > SortedIndexSegment.Level1MaxEntries ? 2
                      : sorted.Count > SortedIndexSegment.Level0MaxEntries ? 1
                      : 0;

            var segPath   = FreeSegmentPath(level);
            var bloomPath = Path.ChangeExtension(segPath, ".bloom");

            var bloom = new PackIndexBloomFilter(sorted.Count);
            foreach (var (hash, _) in sorted)
                bloom.Add(hash);

            await FileAtomicHelper.WriteAtomicAsync(bloomPath, bloom.Serialize(), ct).ConfigureAwait(false);
            await SortedIndexSegment.WriteAsync(segPath, sorted, ct).ConfigureAwait(false);

            replacement = new SegmentList([new SegmentEntry(segPath, new SortedIndexSegment(segPath), bloom)]);
        }

        var previous = _segments;

        // The handover. Segments first: until the log is cleared a reader may combine the new
        // segment with log entries that predate it, and that combination is still correct —
        // a log entry only ever points at a pack this method has not retired yet.
        _segments = replacement;

        CloseLogAppendStream();
        _logDict       = new ConcurrentDictionary<Hash32, IndexEntry>();
        _logEntryCount = 0;
        await File.WriteAllBytesAsync(_logFilePath, [], ct).ConfigureAwait(false);

        // Only now are the replaced segments unreferenced. Their files go immediately (an open
        // mapping outlives the unlink on every platform we target); the mappings themselves are
        // handed to the deferred disposer, because a reader may still be inside one.
        foreach (var entry in previous.Entries)
        {
            RetireSegment(entry.Segment);
            TryDeleteFile(entry.SegmentPath);
            TryDeleteFile(Path.ChangeExtension(entry.SegmentPath, ".bloom"));
        }

        // Any segment file left over from an earlier lifecycle is now stale too: the new segment
        // is authoritative for the whole bucket.
        var namePrefix = Path.GetFileName(_indexFilePrefix);
        var keep = replacement.Entries.Select(static e => e.SegmentPath).ToHashSet(StringComparer.Ordinal);

        foreach (var stale in Directory.EnumerateFiles(_directory, $"{namePrefix}.seg-*.idx").ToArray())
        {
            if (keep.Contains(stale))
                continue;

            TryDeleteFile(stale);
            TryDeleteFile(Path.ChangeExtension(stale, ".bloom"));
        }
    }

    /// <summary>
    /// The first unused segment filename at <paramref name="level"/>.
    ///
    /// <para>
    /// Publishing must not reuse the name of a segment that is still mapped: on Linux the
    /// rename would strand readers on the old inode (harmless but confusing), and on Windows it
    /// would fail outright. A bucket only ever holds a handful of segments, so the first free
    /// slot is found immediately.
    /// </para>
    /// </summary>
    private string FreeSegmentPath(int level)
    {
        var namePrefix = Path.GetFileName(_indexFilePrefix);

        for (var seq = 0; seq < 100; seq++)
        {
            var path = Path.Combine(_directory, $"{namePrefix}.seg-{level}{seq:D2}.idx");
            if (!File.Exists(path))
                return path;
        }

        throw new InvalidOperationException($"No free level-{level} segment slot for bucket '{namePrefix}'.");
    }

    /// <summary>
    /// Deletes pack files that nothing references any more and whose drain window has elapsed,
    /// plus any leftover compaction temporaries.
    ///
    /// <para>
    /// Deletion is gated on a live check of the index rather than on a marker alone. That makes
    /// the operation self-validating: whatever state a crashed or half-finished run left behind,
    /// a pack is only unlinked once the index provably no longer points into it. It also makes
    /// this the recovery path — packs orphaned by a run that died between copying forward and
    /// retiring are picked up here without any journal to replay.
    /// </para>
    /// </summary>
    public async Task<GcPurgeResult> PurgeObsoletePacksAsync(TimeSpan drainWindow, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        var deleted = 0;
        var stillDraining = 0;
        long freed = 0;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Compaction temporaries are never referenced by anything.
            foreach (var tmp in Directory.EnumerateFiles(_directory, $"{Path.GetFileName(_dataFilePrefix)}-*{CompactionTempSuffix}"))
            {
                if (!tmp.EndsWith(CompactionTempSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(tmp) < drainWindow)
                {
                    stillDraining++;
                    continue;
                }

                TryDeleteFile(tmp);
            }

            var index = await SnapshotIndexAsync(ct).ConfigureAwait(false);
            var referencedPacks = index.Values.Select(static e => e.FileNo).ToHashSet();

            var appendFileNo = _currentFileNumber;
            var cutoff = DateTime.UtcNow - drainWindow;
            var packNumbers = EnumeratePackFileNumbers(includeRetired: true).ToArray();

            // "Nothing references this pack" is only evidence of garbage if the index that was
            // consulted actually loaded. A corrupt or missing segment is skipped silently on
            // open, which leaves an empty index that would otherwise condemn every pack in the
            // bucket. Retirement markers stay trustworthy — one is only written after an index
            // swap committed — so those are still honoured; unmarked packs are left for a
            // rebuild to explain.
            var indexLooksUsable = index.Count > 0 || packNumbers.Length == 0;

            foreach (var fileNo in packNumbers)
            {
                ct.ThrowIfCancellationRequested();

                if (fileNo == appendFileNo || referencedPacks.Contains(fileNo))
                    continue;

                var packPath   = PackFilePath(fileNo);
                var markerPath = RetiredMarkerPath(fileNo);
                var hasMarker  = File.Exists(markerPath);

                if (!hasMarker && !indexLooksUsable)
                {
                    stillDraining++;
                    continue;
                }

                // With a marker the drain clock starts at retirement; without one this is an
                // orphan, and its own mtime is the only evidence of when it stopped changing.
                var referenceTime = hasMarker
                    ? File.GetLastWriteTimeUtc(markerPath)
                    : File.GetLastWriteTimeUtc(packPath);

                if (referenceTime > cutoff)
                {
                    stillDraining++;
                    continue;
                }

                long size;
                try { size = new FileInfo(packPath).Length; }
                catch { size = 0; }

                TryDeleteFile(packPath);
                if (hasMarker)
                    TryDeleteFile(markerPath);

                if (!File.Exists(packPath))
                {
                    deleted++;
                    freed += size;
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }

        DrainRetiredSegments();
        return new GcPurgeResult(deleted, freed, stillDraining);
    }

    // -----------------------------------------------------------------------
    // Stats helpers

    /// <summary>
    /// Returns a snapshot of all index entries from the hot log dictionary.
    /// Does not include segment entries.
    /// </summary>
    public Dictionary<Hash32, (int fileNo, long offset, int length)> GetIndexSnapshot()
    {
        return _logDict.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (kvp.Value.FileNo, kvp.Value.Offset, kvp.Value.Length));
    }

    /// <summary>
    /// Returns the total chunk count across the hot log and all loaded segments.
    /// </summary>
    public int GetTotalChunkCount()
    {
        var count = _logEntryCount;
        foreach (var seg in _segments.Entries)
            count += seg.Segment.EntryCount;
        return count;
    }

    public int CountDataFiles()
    {
        var pattern = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";
        return Directory.EnumerateFiles(_directory, pattern).Count();
    }

    public int GetEstimatedUncompressedSize((int fileNo, long offset, int length) entry)
    {
        try
        {
            var path = $"{_dataFilePrefix}-{entry.fileNo}.pack";
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                options: FileOptions.RandomAccess);
            return PackFileEntry.ReadUncompressedLength(fs.SafeFileHandle, entry.offset);
        }
        catch
        {
            return 0;
        }
    }

    // -----------------------------------------------------------------------
    // LRU cache integration

    internal bool TryAcquireLease()
    {
        // Atomically increment the lease count only when the handler has not
        // been marked for dispose (state >= 0).  Spins on contention.
        while (true)
        {
            var current = Volatile.Read(ref _state);
            if (current < 0) // int.MinValue sentinel — dispose requested
                return false;

            if (Interlocked.CompareExchange(ref _state, current + 1, current) == current)
                return true;
        }
    }

    internal void ReleaseLease()
        => Interlocked.Decrement(ref _state);

    internal bool IsIdle => Volatile.Read(ref _state) == 0;

    /// <summary>
    /// Atomically transitions the handler from idle (state == 0) to the
    /// dispose-requested sentinel (int.MinValue).  Returns true only when
    /// the CAS succeeds, guaranteeing that no lease is active and no new
    /// lease can be acquired afterwards.
    /// </summary>
    internal bool TryMarkForDispose()
        => Interlocked.CompareExchange(ref _state, int.MinValue, 0) == 0;

    // -----------------------------------------------------------------------
    // Pack file management

    private async Task<FileStream> GetWritableDataFileAsync()
    {
        if (_currentFileNumber == int.MinValue || _currentStream is null)
        {
            await OpenCurrentWritableFileAsync().ConfigureAwait(false);
            return _currentStream!;
        }

        if (_currentStreamLength >= _maxPackFileSize)
        {
            _currentStream.Dispose();
            _currentStream = null;
            _currentFileNumber = AllocatePackFileNumberUnderLock();
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
        }

        if (_currentStream is null || !_currentStream.CanWrite)
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);

        return _currentStream!;
    }

    private async Task OpenCurrentWritableFileAsync()
    {
        // A retired pack is scheduled for deletion, so it must never be adopted as the
        // append target even when it happens to carry the highest number on disk.
        var existing = EnumeratePackFileNumbers(includeRetired: false)
            .OrderBy(static n => n)
            .ToArray();

        if (existing.Length == 0)
        {
            _currentFileNumber = AllocatePackFileNumberUnderLock();
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
            return;
        }

        var highest     = existing[^1];
        var highestPath = $"{_dataFilePrefix}-{highest}.pack";
        var highestLen  = new FileInfo(highestPath).Length;

        // Seed the allocator from what is already on disk so a fresh handler cannot hand
        // out a number that an earlier process already used.
        _highestAllocatedFileNumber = Math.Max(_highestAllocatedFileNumber, highest);

        _currentFileNumber = highestLen < _maxPackFileSize ? highest : AllocatePackFileNumberUnderLock();
        await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
    }

    /// <summary>
    /// Reserves the next unused pack file number for this bucket.
    /// Must be called under <see cref="_writeLock"/> — it is the single arbiter between the
    /// append path's 4 GiB rollover and compaction's fresh output files.
    /// </summary>
    private int AllocatePackFileNumberUnderLock()
    {
        var onDisk = EnumeratePackFileNumbers(includeRetired: true)
            .DefaultIfEmpty(-1)
            .Max();

        var next = Math.Max(Math.Max(onDisk, _highestAllocatedFileNumber), _currentFileNumber == int.MinValue ? -1 : _currentFileNumber) + 1;
        _highestAllocatedFileNumber = next;
        return next;
    }

    /// <summary>
    /// Pack file numbers present in this bucket's directory, optionally including files that
    /// carry a retirement marker.
    /// </summary>
    private IEnumerable<int> EnumeratePackFileNumbers(bool includeRetired)
    {
        var pattern = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";

        foreach (var path in Directory.EnumerateFiles(_directory, pattern))
        {
            // EnumerateFiles patterns can over-match on Windows short names; require the
            // exact extension so a sidecar never gets mistaken for a pack.
            if (!path.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                continue;

            var no = ParsePackFileNumber(path);
            if (no < 0)
                continue;

            if (!includeRetired && File.Exists(RetiredMarkerPath(no)))
                continue;

            yield return no;
        }
    }

    private string PackFilePath(int fileNo) => $"{_dataFilePrefix}-{fileNo}.pack";

    private string RetiredMarkerPath(int fileNo) => $"{_dataFilePrefix}-{fileNo}{RetiredMarkerSuffix}";

    private Task OpenSpecificWritableFileAsync(int fileNumber)
    {
        var path = $"{_dataFilePrefix}-{fileNumber}.pack";
        _currentStream?.Dispose();
        _currentStream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous);
        _currentStreamLength = _currentStream.Length;
        return Task.CompletedTask;
    }

    private static int ParsePackFileNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        if (dash < 0) return -1;
        return int.TryParse(name[(dash + 1)..], out var n) ? n : -1;
    }

    // -----------------------------------------------------------------------
    // Helpers

    private void DisposeAndClearSegments()
    {
        var old = _segments;
        _segments = SegmentList.Empty;
        foreach (var entry in old.Entries)
            RetireSegment(entry.Segment);
    }

    /// <summary>
    /// Hands a segment that is no longer part of the published list to the deferred disposer.
    /// Never dispose a segment inline: a lock-free reader may be mid-lookup inside it.
    /// </summary>
    private void RetireSegment(SortedIndexSegment segment)
    {
        _retiredSegments.Enqueue((segment, DateTime.UtcNow.Add(SegmentDisposalGrace).Ticks));
        DrainRetiredSegments();
    }

    /// <summary>
    /// Disposes segments whose grace period has elapsed. Cheap enough to call on any
    /// write-path transition; the queue is empty in the steady state.
    /// </summary>
    private void DrainRetiredSegments()
    {
        var now = DateTime.UtcNow.Ticks;

        while (_retiredSegments.TryPeek(out var head) && head.DueTicks <= now)
        {
            if (!_retiredSegments.TryDequeue(out var due))
                break;

            try { due.Segment.Dispose(); }
            catch { /* best-effort: the mapping goes away with the process anyway */ }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IndexedPackFileHandler));
    }

    // -----------------------------------------------------------------------
    // IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CloseLogAppendStream();
        DisposeAndClearSegments();

        _currentStream?.Dispose();
        _currentStream = null;

        _writeLock.Dispose();
        _initLock.Dispose();
    }

    // -----------------------------------------------------------------------
    // Inner types

    /// <summary>A loaded segment, its file path, and its optional bloom filter.</summary>
    private sealed class SegmentEntry
    {
        public string SegmentPath { get; }
        public SortedIndexSegment Segment { get; }
        public PackIndexBloomFilter? Bloom { get; }

        public SegmentEntry(string segmentPath, SortedIndexSegment segment, PackIndexBloomFilter? bloom)
        {
            SegmentPath = segmentPath;
            Segment     = segment;
            Bloom       = bloom;
        }
    }

    /// <summary>
    /// Immutable snapshot of all loaded segments, ordered newest-first.
    /// Swapped atomically via the volatile <see cref="_segments"/> field.
    /// </summary>
    private sealed class SegmentList
    {
        public static readonly SegmentList Empty = new(Array.Empty<SegmentEntry>());

        public IReadOnlyList<SegmentEntry> Entries { get; }

        public SegmentList(SegmentEntry[] entries) => Entries = entries;

        public SegmentList Prepend(SegmentEntry entry)
        {
            var arr = new SegmentEntry[Entries.Count + 1];
            arr[0] = entry;
            for (var i = 0; i < Entries.Count; i++)
                arr[i + 1] = Entries[i];
            return new SegmentList(arr);
        }
    }
}
