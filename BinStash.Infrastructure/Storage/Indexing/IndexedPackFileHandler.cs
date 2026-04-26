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
            seg.Segment.Dispose();
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

    public async Task<int> WriteIndexedDataAsync(Hash32 hash, ReadOnlyMemory<byte> data)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        // Fast-path: lock-free duplicate check
        if (TryFindInIndex(hash, out _))
            return 0;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check under write lock
            if (TryFindInIndex(hash, out _))
                return 0;

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

            return length;
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
    /// Scans all pack files for this prefix, rebuilds the index from scratch,
    /// and writes a single sorted segment (choosing the level based on entry count).
    /// </summary>
    public async Task<bool> RebuildIndexFile()
    {
        await EnsureIndexLoadedAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            var pattern   = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";
            var dataFiles = Directory.EnumerateFiles(_directory, pattern)
                .OrderBy(ParsePackFileNumber)
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

            // Tear down existing index files + state
            DisposeAndClearSegments();
            DeleteAllIndexFiles();
            CloseLogAppendStream();

            _logDict       = new ConcurrentDictionary<Hash32, IndexEntry>();
            _logEntryCount = 0;
            _segments      = SegmentList.Empty;

            if (rebuilt.Count == 0)
                return true;

            var sorted = rebuilt
                .OrderBy(static kvp => kvp.Key)
                .Select(static kvp => (kvp.Key, kvp.Value))
                .ToList();

            var level    = sorted.Count > SortedIndexSegment.Level1MaxEntries ? 2
                         : sorted.Count > SortedIndexSegment.Level0MaxEntries ? 1
                         : 0;
            var namePrefix = Path.GetFileName(_indexFilePrefix);
            var segPath   = Path.Combine(_directory, $"{namePrefix}.seg-{level}00.idx");
            var bloomPath = Path.ChangeExtension(segPath, ".bloom");

            var bloom = new PackIndexBloomFilter(sorted.Count);
            foreach (var (hash, _) in sorted)
                bloom.Add(hash);

            await FileAtomicHelper.WriteAtomicAsync(bloomPath, bloom.Serialize()).ConfigureAwait(false);
            await SortedIndexSegment.WriteAsync(segPath, sorted).ConfigureAwait(false);

            var seg = new SortedIndexSegment(segPath);
            _segments = new SegmentList([new SegmentEntry(segPath, seg, bloom)]);

            return true;
        }
        catch
        {
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
            var pattern   = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";
            var dataFiles = Directory.EnumerateFiles(_directory, pattern)
                .OrderBy(ParsePackFileNumber)
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
            _currentFileNumber++;
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
        }

        if (_currentStream is null || !_currentStream.CanWrite)
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);

        return _currentStream!;
    }

    private async Task OpenCurrentWritableFileAsync()
    {
        var prefix   = Path.GetFileName(_dataFilePrefix);
        var existing = Directory.EnumerateFiles(_directory, $"{prefix}-*.pack")
            .Select(ParsePackFileNumber)
            .Where(static n => n >= 0)
            .OrderBy(static n => n)
            .ToArray();

        if (existing.Length == 0)
        {
            _currentFileNumber = 0;
            await OpenSpecificWritableFileAsync(0).ConfigureAwait(false);
            return;
        }

        var highest     = existing[^1];
        var highestPath = $"{_dataFilePrefix}-{highest}.pack";
        var highestLen  = new FileInfo(highestPath).Length;

        _currentFileNumber = highestLen < _maxPackFileSize ? highest : highest + 1;
        await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
    }

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
            entry.Segment.Dispose();
    }

    private void DeleteAllIndexFiles()
    {
        var namePrefix = Path.GetFileName(_indexFilePrefix);
        foreach (var f in Directory.EnumerateFiles(_directory, $"{namePrefix}.seg-*.idx"))
            TryDeleteFile(f);
        foreach (var f in Directory.EnumerateFiles(_directory, $"{namePrefix}.seg-*.bloom"))
            TryDeleteFile(f);
        TryDeleteFile(_logFilePath);
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
