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

using BinStash.Infrastructure.Helper;

namespace BinStash.Infrastructure.Storage.Stats;

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