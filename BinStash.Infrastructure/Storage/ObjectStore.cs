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
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
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
    private readonly string _BasePath;
    private readonly long _MaxPackSize = 4L * 1024 * 1024 * 1024; // 4 GiB max pack file size
    private readonly Dictionary<string, ChunkFileHandler> _FileHandlers = new();

    public ObjectStore(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            throw new ArgumentException("Storage directory cannot be null or empty.", nameof(basePath));
        
        Directory.CreateDirectory(basePath);

        _BasePath = basePath;
        InitializeFileHandlers();
    }

    private void InitializeFileHandlers()
    {
        for (var i = 0; i < 4096; i++)
        {
            var prefix = i.ToString("x3");
            // The folder structure will look like:
            // basePath/
            //   Chunks/
            //     00/
            //       index00x.idx
            _FileHandlers[prefix] = new ChunkFileHandler(Path.Combine(_BasePath, "Chunks", prefix[..2]), prefix, _MaxPackSize);
        }
    }

    public async Task WriteChunkAsync(byte[] chunkData)
    {
        var hash = ComputeHash(chunkData);
        var prefix = hash[..3];
        await _FileHandlers[prefix].WriteChunkAsync(hash, chunkData);
    }

    public async Task<byte[]> ReadChunkAsync(string hash)
    {
        var prefix = hash[..3];
        return await _FileHandlers[prefix].ReadChunkAsync(hash);
    }

    public async Task WriteReleasePackageAsync(byte[] releasePackageData)
    {
        var hash = ComputeHash(releasePackageData);
        var folder = Path.Join(_BasePath, "Releases", hash[..3]);
        Directory.CreateDirectory(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        await File.WriteAllBytesAsync(filePath, releasePackageData);
    }

    public async Task<byte[]> ReadReleasePackageAsync(string hash)
    {
        var folder = Path.Join(_BasePath, "Releases", hash[..3]);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);
        var filePath = Path.Join(folder, $"{hash}.rdef");
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);
        return await File.ReadAllBytesAsync(filePath);
    }
    
    private static string ComputeHash(byte[] data)
    {
        var hash = Hasher.Hash(data);
        return Convert.ToHexStringLower(hash.AsSpan());
    }
    
    public StorageStatistics GetStatistics()
    {
        var stats = new StorageStatistics();
        var prefixCounts = new Dictionary<string, int>();
    
        foreach (var kvp in _FileHandlers)
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

internal class ChunkFileHandler
{
    private readonly long _MaxPackFileSize;
    private readonly string _IndexFilePath;
    private readonly string _DataFilePrefix;
    private readonly SemaphoreSlim _PackFileLock = new(1, 1);
    private readonly SemaphoreSlim _IndexFileLock = new(1, 1);
    private readonly Dictionary<string, (int fileNo, long offset, int length)> _Index = new();

    public ChunkFileHandler(string directoryPath, string prefix, long maxPackFileSize)
    {
        _MaxPackFileSize = maxPackFileSize;
        Directory.CreateDirectory(directoryPath);
        _IndexFilePath = Path.Combine(directoryPath, $"index{prefix}.idx");
        _DataFilePrefix = Path.Combine(directoryPath, $"chunks{prefix}");
        LoadIndex();
    }

    private void LoadIndex()
    {
        if (!File.Exists(_IndexFilePath)) return;

        _IndexFileLock.Wait();

        try
        {
            var indexLength = new FileInfo(_IndexFilePath).Length;
            using var mmf = MemoryMappedFile.CreateFromFile(_IndexFilePath, FileMode.Open);
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

                _Index[Convert.ToHexStringLower(hash)] = (fileNo, offset, length);
            }
        }
        finally
        {
            _IndexFileLock.Release();
        }
    }
    
    private void SaveIndexEntry(string hash, int fileNumber, long offset, int length)
    {
        _IndexFileLock.Wait();

        try
        {
            using var writer = new BinaryWriter(File.Open(_IndexFilePath, FileMode.Append, FileAccess.Write, FileShare.Read));
            writer.Write(Convert.FromHexString(hash));
            VarIntUtils.WriteVarInt(writer, fileNumber);
            VarIntUtils.WriteVarInt(writer, offset);
            VarIntUtils.WriteVarInt(writer, length);
            writer.Flush();
        }
        finally
        {
            _IndexFileLock.Release();
        }
    }
    
    public Dictionary<string, (int fileNo, long offset, int length)> GetIndexSnapshot()
    {
        return new(_Index);
    }
    
    public int CountDataFiles()
    {
        var count = 0;
        for (var i = 0; ; i++)
        {
            var path = $"{_DataFilePrefix}-{i}.pack";
            if (!File.Exists(path))
                break;
            count++;
        }
        return count;
    }
    
    public int GetEstimatedUncompressedSize((int fileNo, long offset, int length) entry)
    {
        _PackFileLock.Wait();
        try
        {
            var path = $"{_DataFilePrefix}-{entry.fileNo}.pack";
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
            _PackFileLock.Release();
        }
    }
    
    public async Task WriteChunkAsync(string hash, byte[] chunkData)
    {
        await _PackFileLock.WaitAsync();
        try
        {
            if (_Index.ContainsKey(hash))
                return;

            await using var dataStream = GetWritableDataFile(out var fileNo);
            var (offset, length) = await PackFileEntry.WriteAsync(dataStream, chunkData);

            _Index[hash] = (fileNo, offset, length);
            SaveIndexEntry(hash, fileNo, offset, length);
        }
        finally
        {
            _PackFileLock.Release();
        }
    }

    public async Task<byte[]> ReadChunkAsync(string hash)
    {
        await _PackFileLock.WaitAsync();
        try
        {
            if (!_Index.TryGetValue(hash, out var entry))
                throw new KeyNotFoundException("Chunk not found.");

            var path = $"{_DataFilePrefix}-{entry.fileNo}.pack";
            await using var dataStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            dataStream.Seek(entry.offset, SeekOrigin.Begin);
            return await PackFileEntry.ReadAsync(dataStream) ?? throw new InvalidDataException("Failed to read chunk data.");
        }
        finally
        {
            _PackFileLock.Release();
        }
    }

    private FileStream GetWritableDataFile(out int fileNumber)
    {
        for (fileNumber = 0; ; fileNumber++)
        {
            var path = $"{_DataFilePrefix}-{fileNumber}.pack";
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < _MaxPackFileSize)
                return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        }
    }
}

internal static class PackFileEntry
{
    private const int HeaderSize = 21;
    private const uint Magic = 0x4B435342; // ASCII "BSCK" => "BinStash Chunk" / Little-endian encoded
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
    
    public static async Task<byte[]?> ReadAsync(Stream input, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var headerBuf = new byte[HeaderSize];
        var read = await input.ReadAsync(headerBuf.AsMemory(0, HeaderSize), ct);
        if (read == 0) return null;
        if (read != HeaderSize) throw new InvalidDataException("Incomplete header");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0, 4));
        if (magic != Magic) throw new InvalidDataException("Bad magic");

        var version = headerBuf[4];
        if (version != Version) throw new NotSupportedException($"Unsupported version {version}");

        var uncompressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(5, 4));
        var compressedLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(9, 4));
        var expectedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(headerBuf.AsSpan(13, 8));

        var compressed = new byte[compressedLength];
        var totalRead = 0;
        while (totalRead < compressedLength)
        {
            var r = await input.ReadAsync(compressed.AsMemory(totalRead, compressedLength - totalRead), ct);
            if (r == 0) throw new EndOfStreamException("Unexpected EOF in chunk");
            totalRead += r;
        }
        
        var actualChecksum = XxHash3.HashToUInt64(compressed);
        if (actualChecksum != expectedChecksum)
            throw new InvalidDataException("Checksum mismatch – data corrupted");

        return DecompressData(compressed);
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
