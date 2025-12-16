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

namespace BinStash.Core.Chunking;

public class ChunkMapEntry
{
    public required string FilePath { get; init; }
    public required long Offset { get; init; }         // Start byte in file
    public required int Length { get; init; }          // Chunk size
    public required Hash32 Checksum { get; init; }     // SHA256 or content hash
}

public class ChunkData
{
    public required Hash32 Checksum { get; init; }
    public required byte[] Data { get; init; }
}

public enum ChunkAnalysisTarget
{
    Balanced,
    Dedupe,
    Throughput,
    ChunkCount
}

public class AnalysisStats
{
    public string FilePath { get; set; } = null!;
    public List<int> ChunkSizes { get; set; } = new();
    public int TotalChunks { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Avg { get; set; }

    public double StdDev =>
        ChunkSizes.Count == 0
            ? 0
            : Math.Sqrt(ChunkSizes.Average(x => Math.Pow(x - Avg, 2)));
}

/// <summary>
/// Holds the result of a chunk size recommendation analysis.
/// </summary>
public class RecommendationResult
{
    /// <summary>
    /// The recommended minimum chunk size (in bytes).
    /// </summary>
    public int RecommendedMin { get; set; }

    /// <summary>
    /// The recommended average chunk size (in bytes).
    /// </summary>
    public int RecommendedAvg { get; set; }

    /// <summary>
    /// The recommended maximum chunk size (in bytes).
    /// </summary>
    public int RecommendedMax { get; set; }

    /// <summary>
    /// The average size of all observed chunks during benchmarking (in bytes).
    /// </summary>
    public int AvgObservedChunkSize { get; set; }

    /// <summary>
    /// The smallest observed chunk size (in bytes).
    /// </summary>
    public int MinObserved { get; set; }

    /// <summary>
    /// The largest observed chunk size (in bytes).
    /// </summary>
    public int MaxObserved { get; set; }

    /// <summary>
    /// The standard deviation of the observed chunk sizes.
    /// </summary>
    public int StdDev { get; set; }

    /// <summary>
    /// Total number of chunks observed across all files.
    /// </summary>
    public int TotalChunks { get; set; }
    
    /// <summary>
    /// The number of unique chunks observed (based on content hash).
    /// </summary>
    public int UniqueChunks { get; set; }
    
    /// <summary>
    /// The deduplication ratio, calculated as total chunks / unique chunks.
    /// </summary>
    public double DedupeRatio { get; set; }
    
    public string Summary =>
        $"Recommended Chunk Sizes: Min={RecommendedMin}, Avg={RecommendedAvg}, Max={RecommendedMax} " + Environment.NewLine +
        $"| Observed: Avg={AvgObservedChunkSize}, Min={MinObserved}, Max={MaxObserved}, StdDev={StdDev} " + Environment.NewLine +
        $"| Total Chunks: {TotalChunks}";
}