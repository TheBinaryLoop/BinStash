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

namespace BinStash.Core.Storage.Stats;

public sealed class ChunkStoreStatsSnapshotResult
{
    public long ChunkCount { get; set; }
    public long FileDefinitionCount { get; set; }
    public long ReleaseCount { get; set; }

    public long ChunkPackBytes { get; set; }
    public long FileDefinitionPackBytes { get; set; }
    public long ReleasePackageBytes { get; set; }
    public long IndexBytes { get; set; }
    public long PhysicalBytesTotal { get; set; }

    public long UniqueLogicalChunkBytes { get; set; }
    public long UniqueCompressedChunkBytes { get; set; }

    public long ReferencedFileBytes { get; set; }
    public long ReferencedUniqueChunkBytes { get; set; }

    public double CompressionRatio { get; set; }
    public double DeduplicationRatio { get; set; }
    public double EffectiveStorageRatio { get; set; }

    public long CompressionSavedBytes { get; set; }
    public long DeduplicationSavedBytes { get; set; }

    public int ChunkPackFileCount { get; set; }
    public int FileDefinitionPackFileCount { get; set; }
    public int ReleasePackageFileCount { get; set; }
    public int IndexFileCount { get; set; }

    public long VolumeTotalBytes { get; set; }
    public long VolumeFreeBytes { get; set; }

    public long AvgChunkSize { get; set; }
    public long AvgCompressedChunkSize { get; set; }
}