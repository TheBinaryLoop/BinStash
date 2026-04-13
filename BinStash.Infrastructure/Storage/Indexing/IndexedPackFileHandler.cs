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
using System.IO.MemoryMappedFiles;
using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Storage.Packing;

namespace BinStash.Infrastructure.Storage.Indexing;

internal sealed class IndexedPackFileHandler : IDisposable
{
    private readonly long _maxPackFileSize;
    private readonly string _indexFilePath;
    private readonly string _dataFilePrefix;
    private readonly Func<ReadOnlySpan<byte>, Hash32> _computeHash;

    // One-time initialization
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialIndexLoadDone;

    // Serializes append/write-path mutations only
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Protects index stream re-open/rebuild operations
    private readonly SemaphoreSlim _indexStreamLock = new(1, 1);

    // Reads are lock-free against this snapshot-like concurrent map
    private ConcurrentDictionary<Hash32, IndexEntry> _index = new();

    // Current pack append state
    private int _currentFileNumber = int.MinValue;
    private FileStream? _currentStream;
    private long _currentStreamLength;

    // Persistent append stream for index
    private FileStream? _indexAppendStream;
    private BinaryWriter? _indexWriter;

    // LRU
    private int _activeLeaseCount;
    private int _disposeRequested;
    
    private bool _disposed;

    private readonly record struct IndexEntry(int FileNo, long Offset, int Length);

    public IndexedPackFileHandler(string directoryPath, string dataFileName, string prefix, long maxPackFileSize, Func<ReadOnlySpan<byte>, Hash32> computeHash)
    {
        _maxPackFileSize = maxPackFileSize;
        _computeHash = computeHash ?? throw new ArgumentNullException(nameof(computeHash));

        Directory.CreateDirectory(directoryPath);

        _indexFilePath = Path.Combine(directoryPath, $"index{prefix}.idx");
        _dataFilePrefix = Path.Combine(directoryPath, $"{dataFileName}{prefix}");
    }

    private async Task EnsureIndexLoadedAsync()
    {
        if (_initialIndexLoadDone)
            return;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialIndexLoadDone)
                return;

            var loadedIndex = LoadIndexCore();
            _index = loadedIndex;
            _initialIndexLoadDone = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private ConcurrentDictionary<Hash32, IndexEntry> LoadIndexCore()
    {
        var map = new ConcurrentDictionary<Hash32, IndexEntry>();

        if (!File.Exists(_indexFilePath))
            return map;

        var fileInfo = new FileInfo(_indexFilePath);
        if (fileInfo.Length == 0)
            return map;

        using var mmf = MemoryMappedFile.CreateFromFile(_indexFilePath, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        long position = 0;
        var indexLength = fileInfo.Length;

        while (position < indexLength)
        {
            var hashBytes = new byte[32];
            accessor.ReadArray(position, hashBytes, 0, 32);
            position += 32;

            var fileNo = VarIntUtils.ReadVarInt<int>(accessor, ref position);
            var offset = VarIntUtils.ReadVarInt<long>(accessor, ref position);
            var length = VarIntUtils.ReadVarInt<int>(accessor, ref position);

            map[new Hash32(hashBytes)] = new IndexEntry(fileNo, offset, length);
        }

        return map;
    }

    private async Task EnsureIndexAppendStreamOpenAsync()
    {
        if (_indexAppendStream is not null && _indexWriter is not null)
            return;

        await _indexStreamLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_indexAppendStream is not null && _indexWriter is not null)
                return;

            _indexAppendStream?.Dispose();
            _indexWriter?.Dispose();

            _indexAppendStream = new FileStream(
                _indexFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous);

            _indexWriter = new BinaryWriter(_indexAppendStream, System.Text.Encoding.UTF8, leaveOpen: true);
        }
        finally
        {
            _indexStreamLock.Release();
        }
    }

    private async Task SaveIndexEntryAsync(Hash32 hash, int fileNumber, long offset, int length)
    {
        await EnsureIndexAppendStreamOpenAsync().ConfigureAwait(false);

        // write lock is already held by caller on write path / rebuild path
        _indexWriter!.Write(hash.GetBytes());
        VarIntUtils.WriteVarInt(_indexWriter, fileNumber);
        VarIntUtils.WriteVarInt(_indexWriter, offset);
        VarIntUtils.WriteVarInt(_indexWriter, length);
        _indexWriter.Flush();

        // Stronger durability than FlushAsync alone.
        _indexAppendStream!.Flush(flushToDisk: true);
    }
    
    internal bool TryAcquireLease()
    {
        while (true)
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
                return false;

            Interlocked.Increment(ref _activeLeaseCount);

            if (Volatile.Read(ref _disposeRequested) == 0)
                return true;

            Interlocked.Decrement(ref _activeLeaseCount);
        }
    }

    internal void ReleaseLease()
    {
        Interlocked.Decrement(ref _activeLeaseCount);
    }

    internal bool IsIdle => Volatile.Read(ref _activeLeaseCount) == 0;

    internal bool TryMarkForDispose()
    {
        return Interlocked.CompareExchange(ref _disposeRequested, 1, 0) == 0;
    }

    public async Task<bool> RebuildIndexFile()
    {
        await EnsureIndexLoadedAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(_dataFilePrefix)!;
            var pattern = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";

            var dataFiles = Directory.EnumerateFiles(directory, pattern)
                .OrderBy(ParsePackFileNumber)
                .ToArray();

            var rebuilt = new ConcurrentDictionary<Hash32, IndexEntry>();

            // Reset on-disk index file
            _indexWriter?.Dispose();
            _indexWriter = null;

            _indexAppendStream?.Dispose();
            _indexAppendStream = null;

            await File.WriteAllBytesAsync(_indexFilePath, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
            await EnsureIndexAppendStreamOpenAsync().ConfigureAwait(false);

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
                if (fileNo < 0)
                    continue;

                await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(fs).ConfigureAwait(false))
                {
                    var hash = _computeHash(entry.Data);

                    if (!rebuilt.TryAdd(hash, new IndexEntry(fileNo, entry.Offset, entry.Length)))
                        continue; // duplicate content, keep first occurrence

                    await SaveIndexEntryAsync(hash, fileNo, entry.Offset, entry.Length).ConfigureAwait(false);
                }
            }

            _index = rebuilt;
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

    public async Task<bool> RebuildPackFilesAsync()
    {
        await EnsureIndexLoadedAsync().ConfigureAwait(false);
        await _writeLock.WaitAsync().ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(_dataFilePrefix)!;
            var pattern = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";

            var dataFiles = Directory.EnumerateFiles(directory, pattern)
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
                    await PackFileEntry.WriteAsync(tmpFs, entry.Data).ConfigureAwait(false);
                }

                await tmpFs.FlushAsync().ConfigureAwait(false);
                tmpFs.Flush(flushToDisk: true);

                fs.Close();
                tmpFs.Close();

                File.Delete(dataFile);
                File.Move(tmpDataFile, dataFile);
            }

            // Current append stream may now point at replaced files
            _currentStream?.Dispose();
            _currentStream = null;
            _currentFileNumber = int.MinValue;
            _currentStreamLength = 0;

            return await RebuildIndexFile().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Dictionary<Hash32, (int fileNo, long offset, int length)> GetIndexSnapshot()
    {
        return _index.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (kvp.Value.FileNo, kvp.Value.Offset, kvp.Value.Length));
    }

    public int CountDataFiles()
    {
        var directory = Path.GetDirectoryName(_dataFilePrefix)!;
        var pattern = $"{Path.GetFileName(_dataFilePrefix)}-*.pack";
        return Directory.EnumerateFiles(directory, pattern).Count();
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

    public async Task<int> WriteIndexedDataAsync(Hash32 hash, ReadOnlyMemory<byte> data)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        // cheap fast-path before entering write lock
        if (_index.ContainsKey(hash))
            return 0;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // double-check under write lock
            if (_index.ContainsKey(hash))
                return 0;

            var dataStream = await GetWritableDataFileAsync().ConfigureAwait(false);
            var fileNo = _currentFileNumber;

            var (offset, length) = await PackFileEntry.WriteAsync(dataStream, data).ConfigureAwait(false);

            // Strong durability for the pack file
            dataStream.Flush(flushToDisk: true);

            _currentStreamLength += length;

            var entry = new IndexEntry(fileNo, offset, length);

            if (!_index.TryAdd(hash, entry))
                return 0;

            await SaveIndexEntryAsync(hash, fileNo, offset, length).ConfigureAwait(false);

            return length;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<byte[]> ReadIndexedDataAsync(Hash32 hash)
    {
        ThrowIfDisposed();
        await EnsureIndexLoadedAsync().ConfigureAwait(false);

        if (!_index.TryGetValue(hash, out var entry))
            throw new KeyNotFoundException($"No data with index {hash.ToHexString()}.");

        var path = $"{_dataFilePrefix}-{entry.FileNo}.pack";

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, options: FileOptions.Asynchronous | FileOptions.RandomAccess);

        return await PackFileEntry.ReadAtAsync(fs.SafeFileHandle, entry.Offset).ConfigureAwait(false) ?? throw new InvalidDataException($"Failed to read data for index {hash.ToHexString()}.");
    }

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
        {
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
        }

        return _currentStream!;
    }

    private async Task OpenCurrentWritableFileAsync()
    {
        var directory = Path.GetDirectoryName(_dataFilePrefix)!;
        var prefix = Path.GetFileName(_dataFilePrefix);

        var existing = Directory.EnumerateFiles(directory, $"{prefix}-*.pack")
            .Select(ParsePackFileNumber)
            .Where(n => n >= 0)
            .OrderBy(n => n)
            .ToArray();

        if (existing.Length == 0)
        {
            _currentFileNumber = 0;
            await OpenSpecificWritableFileAsync(_currentFileNumber).ConfigureAwait(false);
            return;
        }

        var highest = existing[^1];
        var highestPath = $"{_dataFilePrefix}-{highest}.pack";
        var highestLength = new FileInfo(highestPath).Length;

        _currentFileNumber = highestLength < _maxPackFileSize ? highest : highest + 1;
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
        var fileName = Path.GetFileNameWithoutExtension(path);
        var dash = fileName.LastIndexOf('-');
        if (dash < 0)
            return -1;

        return int.TryParse(fileName[(dash + 1)..], out var fileNo) ? fileNo : -1;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IndexedPackFileHandler));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _indexWriter?.Dispose();
        _indexWriter = null;

        _indexAppendStream?.Dispose();
        _indexAppendStream = null;

        _currentStream?.Dispose();
        _currentStream = null;

        _writeLock.Dispose();
        _indexStreamLock.Dispose();
        _initLock.Dispose();
    }
}