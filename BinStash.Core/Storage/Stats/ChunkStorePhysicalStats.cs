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

public sealed class ChunkStorePhysicalStats
{
    public long ChunkPackBytes { get; set; }
    public long FileDefinitionPackBytes { get; set; }
    public long ReleasePackageBytes { get; set; }
    public long IndexBytes { get; set; }
    public long PhysicalBytesTotal { get; set; }

    public int ChunkPackFileCount { get; set; }
    public int FileDefinitionPackFileCount { get; set; }
    public int ReleasePackageFileCount { get; set; }
    public int IndexFileCount { get; set; }

    public long VolumeTotalBytes { get; set; }
    public long VolumeFreeBytes { get; set; }
}