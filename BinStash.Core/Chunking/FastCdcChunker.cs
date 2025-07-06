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

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace BinStash.Core.Chunking;

public class FastCdcChunker : IChunker
{
    private readonly int _MinSize;
    private readonly int _AvgSize;
    private readonly int _MaxSize;
    private readonly uint _MaskS;
    private readonly uint _MaskL;

    private static readonly uint[] GearTable = new uint[256];

    static FastCdcChunker()
    {
        var rng = new Random(1);
        for (var i = 0; i < 256; i++)
        {
            var buffer = new byte[4];
            rng.NextBytes(buffer);
            GearTable[i] = BitConverter.ToUInt32(buffer, 0);
        }
    }

    public FastCdcChunker(int minSize, int avgSize, int maxSize)
    {
        _MinSize = minSize;
        _AvgSize = avgSize;
        _MaxSize = maxSize;

        var bits = (int)Math.Log(avgSize, 2);
        _MaskS = (1u << (bits + 1)) - 1;
        _MaskL = (1u << (bits - 1)) - 1;
    }
    
    public IReadOnlyList<ChunkMapEntry> GenerateChunkMap(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        if (!stream.CanRead) throw new ArgumentException("File stream must be readable.", nameof(filePath));
        return GenerateChunkMap(stream, cancellationToken).Select(x => 
            new ChunkMapEntry
            {
                FilePath = filePath,
                Offset = x.Offset,
                Length = x.Length,
                Checksum = x.Checksum
            }).ToList();
    }

    public IReadOnlyList<ChunkMapEntry> GenerateChunkMap(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
        
        var chunks = new List<ChunkMapEntry>();
        int b;
        uint hash = 0;
        var chunkStart = 0L;

        while ((b = stream.ReadByte()) != -1)
        {
            hash = (hash << 1) + GearTable[b & 0xFF];
            var currentLength = (int)(stream.Position - chunkStart);

            if ((currentLength >= _MinSize && (hash & _MaskS) == 0) ||
                (currentLength >= _AvgSize && (hash & _MaskL) == 0) ||
                currentLength >= _MaxSize)
            {
                var length = currentLength;
                var offset = chunkStart;
                stream.Seek(chunkStart, SeekOrigin.Begin);
                var data = new byte[length];
                stream.ReadExactly(data, 0, length);
                var checksum = Convert.ToHexString(SHA256.HashData(data));

                chunks.Add(new ChunkMapEntry
                {
                    FilePath = null!, // Not applicable for Stream
                    Offset = offset,
                    Length = length,
                    Checksum = checksum
                });

                stream.Seek(chunkStart + length, SeekOrigin.Begin);
                chunkStart += length;
                hash = 0;
            }
        }

        if (chunkStart < stream.Length)
        {
            var length = (int)(stream.Length - chunkStart);
            stream.Seek(chunkStart, SeekOrigin.Begin);
            var data = new byte[length];
            stream.ReadExactly(data, 0, length);
            var checksum = Convert.ToHexString(SHA256.HashData(data));

            chunks.Add(new ChunkMapEntry
            {
                FilePath = null!, // Not applicable for Stream
                Offset = chunkStart,
                Length = length,
                Checksum = checksum
            });
        }

        return chunks;
    }
    
    public async Task<ChunkData> LoadChunkDataAsync(ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default)
    {
        await using var fs = File.OpenRead(chunkInfo.FilePath);
        return await LoadChunkDataAsync(fs, chunkInfo, cancellationToken);
    }

    public async Task<ChunkData> LoadChunkDataAsync(Stream stream, ChunkMapEntry chunkInfo, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
        if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable.", nameof(stream));
        
        stream.Seek(chunkInfo.Offset, SeekOrigin.Begin);
        
        var buffer = new byte[chunkInfo.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken);
            if (bytesRead == 0) break;
            read += bytesRead;
        }

        return new ChunkData
        {
            Checksum = chunkInfo.Checksum,
            Data = buffer
        };
    }

    public Task<RecommendationResult> RecommendChunkerSettingsForTargetAsync(string folderPath, ChunkAnalysisTarget target, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        log ??= _ => { }; // no-op if not provided
        
        var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException("No files found in the specified folder.");

        log($"📁 Found {files.Length} files under: {folderPath}");
        
        var avgSizesToTest = new[] { 8 * 1024, 16 * 1024, 32 * 1024, 64 * 1024, 128 * 1024, 256 * 1024 };
        var configResults = new List<(int min, int avg, int max, List<int> chunkSizes, HashSet<string> uniqueHashes)>();

        foreach (var avg in avgSizesToTest)
        {
            int min = RoundToNearestPowerOfTwo((int)(avg * 0.15));
            int max = RoundToNearestPowerOfTwo((int)(avg * 6.0));
            
            log($"🔍 Testing config: min={min}, avg={avg}, max={max}");

            var chunkSizes = new ConcurrentBag<int>();
            var hashes = new ConcurrentBag<string>();

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
}