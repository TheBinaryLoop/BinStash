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

using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Storage.Packing;

namespace BinStash.Infrastructure.Storage.Indexing;

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