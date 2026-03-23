// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace BinStash.Core.Entities;

public class ChunkStoreStatsSnapshot
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ChunkStoreId { get; set; }
    public DateTimeOffset CollectedAt { get; set; }

    // Counts
    public long ChunkCount { get; set; }
    public long FileDefinitionCount { get; set; }
    public long ReleaseCount { get; set; }

    // Storage (physical)
    public long ChunkPackBytes { get; set; }
    public long FileDefinitionPackBytes { get; set; }
    public long ReleasePackageBytes { get; set; }
    public long IndexBytes { get; set; }
    public long PhysicalBytesTotal { get; set; }

    // --- LOGICAL SIZES ---

    /// <summary>
    /// Total logical size across ALL releases (includes duplicates across releases)
    /// THIS is what users expect as "total data stored"
    /// </summary>
    public long TotalLogicalBytes { get; set; }

    /// <summary>
    /// Logical size after file-level deduplication (unique files only)
    /// </summary>
    public long UniqueFileBytes { get; set; }

    /// <summary>
    /// Logical size after chunk-level deduplication (unique chunks, uncompressed)
    /// </summary>
    public long UniqueLogicalChunkBytes { get; set; }

    /// <summary>
    /// Compressed size of unique chunks (what is actually stored for chunk data)
    /// </summary>
    public long UniqueCompressedChunkBytes { get; set; }

    /// <summary>
    /// Unique chunk bytes referenced by releases
    /// </summary>
    public long ReferencedUniqueChunkBytes { get; set; }

    // --- RATIOS ---

    /// <summary>
    /// Compression efficiency (uncompressed chunks vs compressed chunks)
    /// </summary>
    public double CompressionRatio { get; set; }

    /// <summary>
    /// Deduplication efficiency (TOTAL logical vs unique chunks)
    /// </summary>
    public double DeduplicationRatio { get; set; }

    /// <summary>
    /// Overall efficiency (TOTAL logical vs physical disk usage)
    /// </summary>
    public double EffectiveStorageRatio { get; set; }

    // --- SAVINGS ---
    
    public long CompressionSavedBytes { get; set; }
    public long DeduplicationSavedBytes { get; set; }

    // --- FILE COUNTS ---
    
    public int ChunkPackFileCount { get; set; }
    public int FileDefinitionPackFileCount { get; set; }
    public int ReleasePackageFileCount { get; set; }
    public int IndexFileCount { get; set; }

    // --- DISK INFO ---
    
    public long VolumeTotalBytes { get; set; }
    public long VolumeFreeBytes { get; set; }

    // --- AVERAGES ---
    
    public long AvgChunkSize { get; set; }
    public long AvgCompressedChunkSize { get; set; }
}