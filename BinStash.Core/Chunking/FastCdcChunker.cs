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
using BinStash.Core.Ingestion.Formats.Zip;

namespace BinStash.Core.Chunking;

internal static class FastCdcConstants
{
    public static readonly uint[] GearTable = CreateGearTable();

    private static uint[] CreateGearTable()
    {
        var table = new uint[256];
        var rng = new Random(1);

        for (var i = 0; i < 256; i++)
        {
            Span<byte> buffer = stackalloc byte[4];
            rng.NextBytes(buffer);
            table[i] = BitConverter.ToUInt32(buffer);
        }

        return table;
    }
}

public class FastCdcChunker : IChunker
{
    private const int MmfThreshold = 16 * 1024 * 1024; // 16MB
    
    private readonly int _minSize;
    private readonly int _avgSize;
    private readonly int _maxSize;
    private readonly uint _maskS;
    private readonly uint _maskL;
    
    public FastCdcChunker(int minSize, int avgSize, int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minSize, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(avgSize, minSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxSize, avgSize);
        
        _minSize = minSize;
        _avgSize = avgSize;
        _maxSize = maxSize;

        var bits = (int)Math.Log(avgSize, 2);
        _maskS = (1u << (bits + 1)) - 1;
        _maskL = (1u << (bits - 1)) - 1;
    }
    
    public IReadOnlyList<ChunkMapEntry> GenerateChunkMap(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        if (!stream.CanRead) throw new ArgumentException("File stream must be readable.", nameof(filePath));

        return GenerateChunkMapInternal(stream, filePath, cancellationToken);
    }

    public IReadOnlyList<ChunkMapEntry> GenerateChunkMap(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

        return GenerateChunkMapInternal(stream, null, cancellationToken);
    }
    
    private IReadOnlyList<ChunkMapEntry> GenerateChunkMapInternal(Stream input, string? filePath = null, CancellationToken cancellationToken = default)
    {
        if (input is FileStream fs && input.CanSeek && input.Length > MmfThreshold)
            return ChunkUsingMemoryMappedFile(fs, filePath, cancellationToken);

        if (input.CanSeek)
            return ChunkUsingBuffer(input, filePath, cancellationToken);

        return ChunkUsingNonSeekableStream(input, filePath, cancellationToken);
    }
    
    private IReadOnlyList<ChunkMapEntry> ChunkUsingBuffer(Stream stream, string? filePath, CancellationToken ct)
    {
        var chunks = new List<(long Offset, byte[] Data)>();
        int b;
        uint hash = 0;
        var chunkStart = 0L;
        var detectionPos = 0L;

        while ((b = stream.ReadByte()) != -1)
        {
            hash = (hash << 1) + FastCdcConstants.GearTable[b & 0xFF];
            detectionPos++;

            var currentLength = (int)(detectionPos - chunkStart);

            if ((currentLength >= _minSize && (hash & _maskS) == 0) ||
                (currentLength >= _avgSize && (hash & _maskL) == 0) ||
                currentLength >= _maxSize)
            {
                stream.Seek(chunkStart, SeekOrigin.Begin);
                var buffer = new byte[currentLength];
                stream.ReadExactly(buffer);
                chunks.Add((chunkStart, buffer));
                chunkStart = detectionPos;
                stream.Position = detectionPos;
                hash = 0;
            }
        }

        if (chunkStart < stream.Length)
        {
            var length = (int)(stream.Length - chunkStart);
            stream.Seek(chunkStart, SeekOrigin.Begin);
            var buffer = new byte[length];
            stream.ReadExactly(buffer);
            chunks.Add((chunkStart, buffer));
        }

        return HashChunksParallel(chunks, filePath, ct);
    }
    
    private IReadOnlyList<ChunkMapEntry> ChunkUsingMemoryMappedFile(FileStream stream, string? filePath, CancellationToken ct)
    {
        var chunks = new List<(long Offset, byte[] Data)>();

        using var mmf = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.Inheritable, true);
        using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        var fileLength = stream.Length;
        var hash = 0u;
        long chunkStart = 0;

        for (long pos = 0; pos < fileLength; pos++)
        {
            var b = view.ReadByte(pos);
            hash = (hash << 1) + FastCdcConstants.GearTable[b & 0xFF];
            var currentLength = (int)(pos + 1 - chunkStart);

            if ((currentLength >= _minSize && (hash & _maskS) == 0) ||
                (currentLength >= _avgSize && (hash & _maskL) == 0) ||
                currentLength >= _maxSize)
            {
                var buffer = new byte[currentLength];
                view.ReadArray(chunkStart, buffer, 0, currentLength);
                chunks.Add((chunkStart, buffer));
                chunkStart = pos + 1;
                hash = 0;
            }
        }

        if (chunkStart < fileLength)
        {
            var length = (int)(fileLength - chunkStart);
            var buffer = new byte[length];
            view.ReadArray(chunkStart, buffer, 0, length);
            chunks.Add((chunkStart, buffer));
        }

        return HashChunksParallel(chunks, filePath, ct);
    }

    private IReadOnlyList<ChunkMapEntry> ChunkUsingNonSeekableStream(Stream stream, string? filePath, CancellationToken ct)
    {
        const int readBufferSize = 64 * 1024;

        var results = new List<ChunkMapEntry>();
        using var streamingChunker = CreateStreamingChunker();

        var readBuffer = new byte[readBufferSize];

        int bytesRead;
        while ((bytesRead = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            streamingChunker.Append(readBuffer.AsSpan(0, bytesRead));

            foreach (var boundary in streamingChunker.GetCompletedChunks())
            {
                results.Add(new ChunkMapEntry
                {
                    FilePath = filePath ?? string.Empty,
                    Offset = boundary.Offset,
                    Length = boundary.Length,
                    Checksum = boundary.Checksum
                });
            }
        }

        streamingChunker.Complete();

        foreach (var boundary in streamingChunker.GetCompletedChunks())
        {
            results.Add(new ChunkMapEntry
            {
                FilePath = filePath ?? string.Empty,
                Offset = boundary.Offset,
                Length = boundary.Length,
                Checksum = boundary.Checksum
            });
        }

        return results;
    }
    
    private static IReadOnlyList<ChunkMapEntry> HashChunksParallel(List<(long Offset, byte[] Data)> chunks, string? filePath, CancellationToken ct)
    {
        var results = new ChunkMapEntry[chunks.Count];
        Parallel.For(0, chunks.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var (offset, data) = chunks[i];
            results[i] = new ChunkMapEntry
            {
                FilePath = filePath ?? string.Empty,
                Offset = offset,
                Length = data.Length,
                Checksum = new Hash32(Blake3.Hasher.Hash(data).AsSpan())
            };
        });

        return results;
    }
    
    public async Task<ChunkData> LoadChunkDataAsync(ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default)
    {
        if (TryParseContainerEntryLocator(chunkInfo.FilePath, out var containerType, out var archivePath, out var entryPath))
        {
            return containerType switch
            {
                "zip" => await LoadChunkDataFromZipEntryAsync(archivePath, entryPath, chunkInfo, cancellationToken),
                _ => throw new NotSupportedException($"Unsupported container locator type: {containerType}")
            };
        }

        await using var fs = File.OpenRead(chunkInfo.FilePath);
        return await LoadChunkDataAsync(fs, chunkInfo, cancellationToken);
    }

    public async Task<ChunkData> LoadChunkDataAsync(Stream stream, ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

        if (stream.CanSeek)
        {
            stream.Seek(chunkInfo.Offset, SeekOrigin.Begin);
        }
        else
        {
            await SkipExactlyAsync(stream, chunkInfo.Offset, cancellationToken);
        }

        var buffer = new byte[chunkInfo.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (bytesRead == 0)
                throw new EndOfStreamException($"Unexpected EOF while reading chunk at offset {chunkInfo.Offset} with length {chunkInfo.Length}.");

            read += bytesRead;
        }

        var actualChecksum = new Hash32(Blake3.Hasher.Hash(buffer).AsSpan());
        if (actualChecksum != chunkInfo.Checksum)
        {
            throw new InvalidDataException($"Chunk checksum mismatch while loading chunk at offset {chunkInfo.Offset} from '{chunkInfo.FilePath}'. Expected {chunkInfo.Checksum}, got {actualChecksum}.");
        }

        return new ChunkData
        {
            Checksum = chunkInfo.Checksum,
            Data = buffer
        };
    }

    public IStreamingChunker CreateStreamingChunker()
        => new FastCdcStreamingChunker(_minSize, _avgSize, _maxSize, _maskS, _maskL);

    public Task<RecommendationResult> RecommendChunkerSettingsForTargetAsync(string folderPath, ChunkAnalysisTarget target, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        log ??= _ => { }; // no-op if not provided
        
        var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException("No files found in the specified folder.");

        log($"📁 Found {files.Length} files under: {folderPath}");
        
        var avgSizesToTest = new[] { 8 * 1024, 16 * 1024, 32 * 1024, 64 * 1024, 128 * 1024, 256 * 1024 };
        var configResults = new List<(int min, int avg, int max, List<int> chunkSizes, HashSet<Hash32> uniqueHashes)>();

        foreach (var avg in avgSizesToTest)
        {
            int min = RoundToNearestPowerOfTwo((int)(avg * 0.15));
            int max = RoundToNearestPowerOfTwo((int)(avg * 6.0));
            
            log($"🔍 Testing config: min={min}, avg={avg}, max={max}");

            var chunkSizes = new ConcurrentBag<int>();
            var hashes = new ConcurrentBag<Hash32>();

            Parallel.ForEach(files, file =>
            {
                using var fs = File.OpenRead(file);
                var chunker = new FastCdcChunker(min, avg, max);
                var map = chunker.GenerateChunkMap(fs, cancellationToken);

                foreach (var entry in map)
                {
                    chunkSizes.Add(entry.Length);
                    hashes.Add(entry.Checksum); // SHA256 already generated by your chunker
                }
            });

            configResults.Add((min, avg, max, chunkSizes.ToList(), hashes.ToHashSet()));
            
            log($"  ✅ Done: {chunkSizes.Count} total chunks, {hashes.Distinct().Count()} unique");
        }
        
        log($"📊 Scoring {configResults.Count} configurations for target: {target}");

        var best = configResults
            .Select(cfg =>
            {
                var observedAvg = (int)cfg.chunkSizes.Average();
                var stdDev = Math.Sqrt(cfg.chunkSizes.Average(x => Math.Pow(x - observedAvg, 2)));
                int totalChunks = cfg.chunkSizes.Count;
                int uniqueChunks = cfg.uniqueHashes.Count;

                double dedupeRatio = uniqueChunks == 0 ? 1.0 : (double)totalChunks / uniqueChunks;

                int score = target switch
                {
                    ChunkAnalysisTarget.Dedupe     => (int)(dedupeRatio * 1000), // higher is better
                    ChunkAnalysisTarget.Throughput => -totalChunks,
                    ChunkAnalysisTarget.ChunkCount => -totalChunks,
                    ChunkAnalysisTarget.Balanced   => (int)(stdDev + Math.Abs(cfg.avg - observedAvg)),
                    _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
                };
                
                log($"  📦 Config [min={cfg.min}, avg={cfg.avg}, max={cfg.max}] ⇒ chunks={totalChunks}, unique={uniqueChunks}, score={score}");

                return new
                {
                    cfg.min,
                    cfg.avg,
                    cfg.max,
                    ObservedAvg = observedAvg,
                    ObservedMin = cfg.chunkSizes.Min(),
                    ObservedMax = cfg.chunkSizes.Max(),
                    StdDev = (int)stdDev,
                    TotalChunks = totalChunks,
                    UniqueChunks = uniqueChunks,
                    DedupeRatio = dedupeRatio,
                    Score = score
                };
            })
            .OrderByDescending(r => r.Score) // best score = highest
            .First();

        log($"\n🏆 Best config selected: min={best.min}, avg={best.avg}, max={best.max}");
        log($"   → Total chunks: {best.TotalChunks}, unique: {best.UniqueChunks}, dedupe ratio: {best.DedupeRatio:F2}");
        
        return Task.FromResult(new RecommendationResult
        {
            RecommendedMin = best.min,
            RecommendedAvg = best.avg,
            RecommendedMax = best.max,
            AvgObservedChunkSize = best.ObservedAvg,
            MinObserved = best.ObservedMin,
            MaxObserved = best.ObservedMax,
            StdDev = best.StdDev,
            TotalChunks = best.TotalChunks,
            UniqueChunks = best.UniqueChunks,
            DedupeRatio = best.DedupeRatio
        });
    }
    
    private static int RoundToNearestPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value)
            power <<= 1;
        return power;
    }
    
    private static bool TryParseContainerEntryLocator(string locator, out string containerType, out string archivePath, out string entryPath)
    {
        containerType = string.Empty;
        archivePath = string.Empty;
        entryPath = string.Empty;

        if (string.IsNullOrWhiteSpace(locator))
            return false;

        var firstSep = locator.IndexOf('|');
        if (firstSep < 0)
            return false;

        var secondSep = locator.IndexOf('|', firstSep + 1);
        if (secondSep < 0)
            return false;

        containerType = locator[..firstSep];
        archivePath = locator.Substring(firstSep + 1, secondSep - firstSep - 1);
        entryPath = locator[(secondSep + 1)..];

        return !string.IsNullOrWhiteSpace(containerType) && !string.IsNullOrWhiteSpace(archivePath) && !string.IsNullOrWhiteSpace(entryPath);
    }

    private async Task<ChunkData> LoadChunkDataFromZipEntryAsync(string zipFilePath, string entryPath, ChunkMapEntry chunkInfo, CancellationToken cancellationToken)
    {
        await using var stream = OpenZipEntryStream(zipFilePath, entryPath);
        return await LoadChunkDataAsync(stream, chunkInfo, cancellationToken);
    }

    private static Stream OpenZipEntryStream(string zipFilePath, string entryPath)
    {
        return new ZipEntryStreamFactory().Create(zipFilePath, entryPath)();
    }

    private static async Task SkipExactlyAsync(Stream stream, long bytesToSkip, CancellationToken cancellationToken)
    {
        if (bytesToSkip < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesToSkip));

        if (bytesToSkip == 0)
            return;

        var buffer = new byte[64 * 1024];
        long remaining = bytesToSkip;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException($"Unexpected EOF while skipping {bytesToSkip} bytes.");

            remaining -= read;
        }
    }
}

internal sealed class FastCdcStreamingChunker : IStreamingChunker
{
    private readonly int _minSize;
    private readonly int _avgSize;
    private readonly int _maxSize;
    private readonly uint _maskS;
    private readonly uint _maskL;
    
    private readonly List<(long Offset, int Length, Hash32 Checksum)> _chunks = new();

    private Blake3.Hasher _currentChunkHasher = Blake3.Hasher.New();

    private long _currentChunkStart;
    private int _currentChunkLength;
    private int _completedChunksConsumed;
    private long _totalBytesProcessed;
    private uint _rollingHash;
    private bool _completed;

    public FastCdcStreamingChunker(int minSize, int avgSize, int maxSize, uint maskS, uint maskL)
    {
        _minSize = minSize;
        _avgSize = avgSize;
        _maxSize = maxSize;
        _maskS = maskS;
        _maskL = maskL;
    }

    public void Append(ReadOnlySpan<byte> buffer)
    {
        if (_completed)
            throw new InvalidOperationException("Cannot append after completion.");

        if (buffer.IsEmpty)
            return;

        var segmentStart = 0;

        for (var i = 0; i < buffer.Length; i++)
        {
            var b = buffer[i];
            _rollingHash = (_rollingHash << 1) + FastCdcConstants.GearTable[b];
            _currentChunkLength++;
            _totalBytesProcessed++;

            var shouldCut =
                (_currentChunkLength >= _minSize && (_rollingHash & _maskS) == 0) ||
                (_currentChunkLength >= _avgSize && (_rollingHash & _maskL) == 0) ||
                _currentChunkLength >= _maxSize;

            if (!shouldCut)
                continue;

            // Feed the remaining bytes of the current chunk from this input buffer.
            var segmentLength = i - segmentStart + 1;
            if (segmentLength > 0)
                _currentChunkHasher.Update(buffer.Slice(segmentStart, segmentLength));

            FinalizeCurrentChunk();

            segmentStart = i + 1;
        }

        // Feed any trailing bytes that belong to the still-open chunk.
        if (segmentStart < buffer.Length)
            _currentChunkHasher.Update(buffer[segmentStart..]);
    }

    public IReadOnlyList<ChunkBoundary> Complete()
    {
        if (_completed)
            throw new InvalidOperationException("Complete has already been called.");

        _completed = true;

        if (_currentChunkLength > 0)
            FinalizeCurrentChunk();

        return _chunks
            .Select(x => new ChunkBoundary
            {
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            })
            .ToArray();
    }

    private void FinalizeCurrentChunk()
    {
        if (_currentChunkLength <= 0)
            return;

        var checksum = new Hash32(_currentChunkHasher.Finalize().AsSpan());

        _chunks.Add((
            Offset: _currentChunkStart,
            Length: _currentChunkLength,
            Checksum: checksum));

        _currentChunkStart = _totalBytesProcessed;
        _currentChunkLength = 0;
        _rollingHash = 0;
        _currentChunkHasher = Blake3.Hasher.New();
    }

    public IReadOnlyList<ChunkBoundary> GetCompletedChunks()
    {
        if (_completedChunksConsumed >= _chunks.Count)
            return [];

        var newChunks = _chunks
            .Skip(_completedChunksConsumed)
            .Select(x => new ChunkBoundary
            {
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            })
            .ToArray();

        _completedChunksConsumed = _chunks.Count;
        return newChunks;
    }
    
    public void Dispose()
    {
        // Nothing unmanaged here, but keeping IDisposable matches the interface
        // and gives flexibility if you later pool resources.
    }
}