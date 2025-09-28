// Copyright (C) 2025  Lukas Eßmann
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

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Helper;
using Blake3;
using ZstdNet;

namespace BinStash.Infrastructure.Storage;

public static class ObjectStoreManager
{
    private static readonly ConcurrentDictionary<string, ObjectStore> ObjectStores = new();
    
    public static ObjectStore GetOrCreateChunkStorage(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
        
        return ObjectStores.GetOrAdd(basePath, path => new ObjectStore(path));
    }
}

public class ObjectStore
{
    private const long MaxPackSize = 4L * 1024 * 1024 * 1024; // 4 GiB max pack file size
    
    private readonly string _basePath;
    private readonly Dictionary<string, IndexedPackFileHandler> _chunkFileHandlers = new();
    private readonly Dictionary<string, IndexedPackFileHandler> _fileDefinitionFileHandlers = new();

    public ObjectStore(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Storage directory cannot be null or empty.", nameof(basePath));
        
        Directory.CreateDirectory(basePath);

        _basePath = basePath;
        InitializeFileHandlers();
    }

    private void InitializeFileHandlers()
    {
        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            // The folder structure will look like:
            // basePath/
            //   (Chunks|FileDefs)/
            //     00/
            //       index00x.idx
            //       chunks00x-n.pack
            _chunkFileHandlers[prefix] = new IndexedPackFileHandler(Path.Combine(_basePath, "Chunks", prefix[..2]), "chunks", prefix, MaxPackSize, ComputeHash);
            _fileDefinitionFileHandlers[prefix] = new IndexedPackFileHandler(Path.Combine(_basePath, "FileDefs", prefix[..2]), "fileDefs", prefix, MaxPackSize, ComputeHash);
        }
    }
    
    public async Task<bool> RebuildStorageAsync()
    {
        var tasks = new List<Task<bool>>();
        /*foreach (var handler in _chunkFileHandlers.Values)
        {
            tasks.Add(handler.RebuildPackFilesAsync());
        }
        foreach (var handler in _fileDefinitionFileHandlers.Values)
        {
            tasks.Add(handler.RebuildPackFilesAsync());
        }
        var results = await Task.WhenAll(tasks);
        if (!results.All(r => r))
            return false;
        tasks.Clear();*/
        var results = Array.Empty<bool>();
        foreach (var handler in _chunkFileHandlers.Values)
        {
            tasks.Add(handler.RebuildIndexFile());
        }
        foreach (var handler in _fileDefinitionFileHandlers.Values)
        {
            tasks.Add(handler.RebuildIndexFile());
        }
        results = await Task.WhenAll(tasks);
        
        return results.All(r => r);
    }

    public async Task<int> WriteChunkAsync(byte[] chunkData)
    {
        var hash = ComputeHash(chunkData);
        var stringHash = hash.ToHexString();
        var prefix = stringHash[..3];
        return await _chunkFileHandlers[prefix].WriteIndexedDataAsync(hash, chunkData);
    }

    public async Task<byte[]> ReadChunkAsync(string hash)
    {
        var prefix = hash[..3];
        return await _chunkFileHandlers[prefix].ReadIndexedDataAsync(Hash32.FromHexString(hash));
    }
    
    public async Task<int> WriteFileDefinitionAsync(Hash32 fileHash, byte[] fileDefinitionData)
    {
        var stringHash = fileHash.ToHexString();
        var prefix = stringHash[..3];
        return await _fileDefinitionFileHandlers[prefix].WriteIndexedDataAsync(fileHash, fileDefinitionData);
    }
    
    public Task<byte[]> ReadFileDefinitionAsync(string hash)
    {
        var prefix = hash[..3];
        return _fileDefinitionFileHandlers[prefix].ReadIndexedDataAsync(Hash32.FromHexString(hash));
    }

    public async Task WriteReleasePackageAsync(byte[] releasePackageData)
    {
        var hash = ComputeHash(releasePackageData).ToHexString();
        var folder = Path.Join(_basePath, "Releases", hash[..3]);
        Directory.CreateDirectory(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        await File.WriteAllBytesAsync(filePath, releasePackageData);
    }

    public async Task<byte[]> ReadReleasePackageAsync(string hash)
    {
        var folder = Path.Join(_basePath, "Releases", hash[..3]);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);
        return await File.ReadAllBytesAsync(filePath);
    }
    
    private static Hash32 ComputeHash(byte[] data)
    {
        var hash = Hasher.Hash(data);
        return new Hash32(hash.AsSpan());
    }
    
    public StorageStatistics GetStatistics()
    {
        var stats = new StorageStatistics();
        var prefixCounts = new Dictionary<string, int>();
    
        foreach (var kvp in _chunkFileHandlers)
        {
            var handler = kvp.Value;
            var prefix = kvp.Key;
            var index = handler.GetIndexSnapshot();

            stats.TotalChunks += index.Count;
            prefixCounts[prefix] = index.Count;

            foreach (var entry in index.Values)
            {
                stats.TotalCompressedSize += entry.length;
                stats.TotalUncompressedSize += handler.GetEstimatedUncompressedSize(entry);
            }

            stats.TotalFiles += handler.CountDataFiles();
        }

        stats.PrefixChunkCounts = prefixCounts;
        return stats;
    }
}

internal class IndexedPackFileHandler
{
    private readonly long _maxPackFileSize;
    private readonly string _indexFilePath;
    private readonly string _dataFilePrefix;
    private readonly SemaphoreSlim _packFileLock = new(1, 1);
    private readonly SemaphoreSlim _indexFileLock = new(1, 1);
    private readonly Dictionary<Hash32, (int fileNo, long offset, int length)> _index = new();
    
    private readonly Func<byte[], Hash32> _computeHash;
    
    private int _currentFileNumber = int.MinValue;

    public IndexedPackFileHandler(string directoryPath, string dataFileName, string prefix, long maxPackFileSize, Func<byte[], Hash32> computeHash)
    {
        _maxPackFileSize = maxPackFileSize;
        _computeHash = computeHash;
        Directory.CreateDirectory(directoryPath);
        _indexFilePath = Path.Combine(directoryPath, $"index{prefix}.idx");
        _dataFilePrefix = Path.Combine(directoryPath, $"{dataFileName}{prefix}");
        LoadIndex();
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexFilePath)) return;
        if (new FileInfo(_indexFilePath).Length == 0) return;

        _indexFileLock.Wait();

        // Maybe add a file header to the index file in the future to detect corruption
        
        try
        {
            var indexLength = new FileInfo(_indexFilePath).Length;
            using var mmf = MemoryMappedFile.CreateFromFile(_indexFilePath, FileMode.Open);
            using var accessor = mmf.CreateViewAccessor();
            long position = 0;
            while (position < indexLength)
            {
                var hash = new byte[32];
                accessor.ReadArray(position, hash, 0, 32);
                position += 32;

                var fileNo = VarIntUtils.ReadVarInt<int>(accessor, ref position);
                var offset = VarIntUtils.ReadVarInt<long>(accessor, ref position);
                var length = VarIntUtils.ReadVarInt<int>(accessor, ref position);

                _index[new Hash32(hash)] = (fileNo, offset, length);
            }
        }
        finally
        {
            _indexFileLock.Release();
        }
    }
    
    private void SaveIndexEntry(Hash32 hash, int fileNumber, long offset, int length, bool noLock = false)
    {
        if (!noLock)
            _indexFileLock.Wait();

        try
        {
            using var writer = new BinaryWriter(File.Open(_indexFilePath, FileMode.Append, FileAccess.Write, FileShare.Read));
            writer.Write(hash.GetBytes());
            VarIntUtils.WriteVarInt(writer, fileNumber);
            VarIntUtils.WriteVarInt(writer, offset);
            VarIntUtils.WriteVarInt(writer, length);
            writer.Flush();
        }
        finally
        {
            if (!noLock)
                _indexFileLock.Release();
        }
    }

    public async Task<bool> RebuildIndexFile()
    {
        await _indexFileLock.WaitAsync();
        try
        {
            // Find all data files
            var dataFiles = Directory.EnumerateFiles(Path.GetDirectoryName(_dataFilePrefix)!, $"{Path.GetFileName(_dataFilePrefix)}-*.pack");
            
            // Clear in-memory index
            _index.Clear();
            // Clear existing index file
            await File.WriteAllBytesAsync(_indexFilePath, ReadOnlyMemory<byte>.Empty);
            
            // Rebuild index from data files
            foreach (var dataFile in dataFiles)
            {
                Console.WriteLine($"Rebuilding index for {dataFile}");
                await using var fs = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(fs))
                {
                    var hash = _computeHash(entry.Data);
                    if (_index.ContainsKey(hash))
                        continue; // Skip duplicates
                    var fileNoStr = Path.GetFileName(dataFile).Split('-').Last().Split('.').First();
                    if (!int.TryParse(fileNoStr, out var fileNo))
                        continue; // Skip invalid file names
                    _index[hash] = (fileNo, entry.Offset, entry.Length);
                    SaveIndexEntry(hash, fileNo, entry.Offset, entry.Length, noLock: true);
                }
            }
           
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _indexFileLock.Release();
        }
    }
    
    public async Task<bool> RebuildPackFilesAsync()
    {
        await _packFileLock.WaitAsync();
        try
        {
            // Find all data files
            var dataFiles = Directory.EnumerateFiles(Path.GetDirectoryName(_dataFilePrefix)!, $"{Path.GetFileName(_dataFilePrefix)}-*.pack");

            // Rebuild index from data files
            foreach (var dataFile in dataFiles)
            {
                var tmpDataFile = dataFile + ".tmp";
                await using var fs = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var tmpFs = new FileStream(tmpDataFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await foreach (var entry in PackFileEntry.ReadAllEntriesAsync(fs, ignoreChecks: true))
                {
                    await PackFileEntry.WriteAsync(tmpFs, entry.Data);
                }
                tmpFs.Flush();
                tmpFs.Close();
                fs.Close();
                File.Delete(dataFile);
                File.Move(tmpDataFile, dataFile);
            }
            
            return true;
        }
        finally
        {
            _packFileLock.Release();
        }
    }
    
    public Dictionary<Hash32, (int fileNo, long offset, int length)> GetIndexSnapshot()
    {
        return new(_index);
    }
    
    public int CountDataFiles()
    {
        var count = 0;
        for (var i = 0; ; i++)
        {
            var path = $"{_dataFilePrefix}-{i}.pack";
            if (!File.Exists(path))
                break;
            count++;
        }
        return count;
    }
    
    public int GetEstimatedUncompressedSize((int fileNo, long offset, int length) entry)
    {
        _packFileLock.Wait();
        try
        {
            var path = $"{_dataFilePrefix}-{entry.fileNo}.pack";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(entry.offset + 5, SeekOrigin.Begin); // Skip Magic (4) + Version (1)
            Span<byte> lenBuf = stackalloc byte[4];
            fs.ReadExactly(lenBuf);
            return BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        }
        catch
        {
            return 0; // On failure, ignore
        }
        finally
        {
            _packFileLock.Release();
        }
    }
    
    public async Task<int> WriteIndexedDataAsync(Hash32 hash, byte[] data)
    {
        // quick check without lock
        if (_index.ContainsKey(hash))
            return 0;
        
        await _packFileLock.WaitAsync();
        try
        {
            // double-check
            if (_index.ContainsKey(hash))
                return 0;

            await using var dataStream = GetWritableDataFile(out var fileNo);
            var (offset, length) = await PackFileEntry.WriteAsync(dataStream, data);

            _index[hash] = (fileNo, offset, length);
            SaveIndexEntry(hash, fileNo, offset, length);
            return length;
        }
        finally
        {
            _packFileLock.Release();
        }
    }

    public async Task<byte[]> ReadIndexedDataAsync(Hash32 hash)
    {
        await _packFileLock.WaitAsync();
        try
        {
            if (!_index.TryGetValue(hash, out var entry))
                throw new KeyNotFoundException($"No data with index {hash.ToHexString()}.");

            var path = $"{_dataFilePrefix}-{entry.fileNo}.pack";
            await using var dataStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            dataStream.Seek(entry.offset, SeekOrigin.Begin);
            return await PackFileEntry.ReadAsync(dataStream) ?? throw new InvalidDataException($"Failed to read data for index {hash.ToHexString()}.");
        }
        finally
        {
            _packFileLock.Release();
        }
    }

    private FileStream GetWritableDataFile(out int fileNumber)
    {
        // If we haven't determined the current file number yet, do so now
        if (_currentFileNumber == int.MinValue)
        {
            for (fileNumber = 0; ; fileNumber++)
            {
                var path = $"{_dataFilePrefix}-{fileNumber}.pack";
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < _maxPackFileSize)
                {
                    _currentFileNumber = fileNumber;
                    return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                }
            }
        }
        
        // Check if the current file has reached the max size
        if (new FileInfo($"{_dataFilePrefix}-{_currentFileNumber}.pack").Length >= _maxPackFileSize)
        {
            // If so, increment to the next file number
            _currentFileNumber++;
        }
        
        // Return a stream to the current file
        fileNumber = _currentFileNumber;
        return new FileStream($"{_dataFilePrefix}-{fileNumber}.pack", FileMode.Append, FileAccess.Write, FileShare.Read);
    }
}

internal static class PackFileEntry
{
    private const int HeaderSize = 21;
    private const uint Magic = 0x4B505342; // ASCII "BSPK" => "BinStash PackFile" / Little-endian encoded
    private const byte Version = 1;
    
    public static async Task<(long, int)> WriteAsync(Stream output, byte[] data, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        
        var offset = output.Position;
        
        var compressedData = CompressData(data);
        var compressedDataChecksum = XxHash3.HashToUInt64(compressedData);
        var uncompressedLength = data.Length;
        var compressedLength = compressedData.Length;
        
        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header[..4], Magic);              // 0–3
        header[4] = Version;                                                       // 4
        BinaryPrimitives.WriteUInt32LittleEndian(header[5..9], (uint)uncompressedLength); // 5–8
        BinaryPrimitives.WriteUInt32LittleEndian(header[9..13], (uint)compressedLength);  // 9–12
        BinaryPrimitives.WriteUInt64LittleEndian(header[13..21], compressedDataChecksum); // 13–20

        await output.WriteAsync(header.ToArray(), ct);
        await output.WriteAsync(compressedData.AsMemory(0, compressedLength), ct);
        await output.FlushAsync(ct);
        
        return (offset, (int)(output.Position - offset));
    }
    
    public static async Task<byte[]?> ReadAsync(Stream input, bool ignoreChecks = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var headerBuf = new byte[HeaderSize];
        var read = await input.ReadAsync(headerBuf.AsMemory(0, HeaderSize), ct);
        if (read == 0) return null;
        if (read != HeaderSize) throw new InvalidDataException("Incomplete header");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0, 4));
        if (!ignoreChecks && magic != Magic) throw new InvalidDataException("Bad magic");

        var version = headerBuf[4];
        if (!ignoreChecks && version != Version) throw new NotSupportedException($"Unsupported version {version}");

        var uncompressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(5, 4));
        var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(9, 4));
        var expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(headerBuf.AsSpan(13, 8));

        var compressed = new byte[compressedLength];
        var totalRead = 0;
        while (totalRead < compressedLength)
        {
            var r = await input.ReadAsync(compressed.AsMemory(totalRead, compressedLength - totalRead), ct);
            if (r == 0) throw new EndOfStreamException("Unexpected EOF in pack file entry");
            totalRead += r;
        }
        
        var actualChecksum = XxHash3.HashToUInt64(compressed);
        if (actualChecksum != expectedChecksum)
            throw new InvalidDataException("Checksum mismatch – data corrupted");

        var decompressed = DecompressData(compressed);
        if (decompressed.Length != uncompressedLength)
            throw new InvalidDataException("Decompressed length mismatch – data corrupted");
        
        return decompressed;
    }
    
    public static async IAsyncEnumerable<(long Offset, int Length, byte[] Data)> ReadAllEntriesAsync(Stream input, bool ignoreChecks = false, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        
        while (true)
        {
            var offset = input.Position;
            var entry = await ReadAsync(input, ignoreChecks, ct);
            if (entry == null) yield break;
            var length = (int)(input.Position - offset);
            yield return (offset, length, entry);
        }
    }
    
    private static byte[] CompressData(byte[] data)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(data);
    }

    private static byte[] DecompressData(byte[] compressedData)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressedData);
    }
}

public class StorageStatistics
{
    public int TotalChunks { get; set; }
    public long TotalCompressedSize { get; set; }
    public long TotalUncompressedSize { get; set; }
    public double CompressionRatio => TotalUncompressedSize == 0 ? 1 : (double)TotalUncompressedSize / TotalCompressedSize;
    public int TotalFiles { get; set; }
    public double AvgCompressedChunkSize => TotalChunks == 0 ? 0 : (double)TotalCompressedSize / TotalChunks;
    public double AvgUncompressedChunkSize => TotalChunks == 0 ? 0 : (double)TotalUncompressedSize / TotalChunks;
    public Dictionary<string, int> PrefixChunkCounts { get; set; } = new();
    
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "TotalPackFiles", TotalFiles },
            { "TotalChunks", TotalChunks },
            { "AvgChunksPerPackFile", $"{(TotalFiles == 0 ? 0 : (double)TotalChunks / TotalFiles):0.00}" },
            { "TotalCompressedSize", BytesConverter.BytesToHuman(TotalCompressedSize) },
            { "TotalUncompressedSize", BytesConverter.BytesToHuman(TotalUncompressedSize) },
            { "CompressionRatio", $"{CompressionRatio:0.00}" },
            { "AvgCompressedChunkSize", BytesConverter.BytesToHuman((long)AvgCompressedChunkSize) },
            { "AvgUncompressedChunkSize", BytesConverter.BytesToHuman((long)AvgUncompressedChunkSize) },
            { "SpaceSavings", $"{(TotalUncompressedSize == 0 ? 0 : (double)(TotalUncompressedSize - TotalCompressedSize) / TotalUncompressedSize * 100):0.00}%" },
            { "long.MaxValue in storage", $"{BytesConverter.BytesToHuman(long.MaxValue)}" },
            //{ "PrefixChunkCounts", PrefixChunkCounts }
        };
    }
}
