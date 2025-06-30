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

namespace BinStash.Core.Chunking;

public class ChunkMapEntry
{
    public required string FilePath { get; init; }
    public required long Offset { get; init; }         // Start byte in file
    public required int Length { get; init; }          // Chunk size
    public required string Checksum { get; init; }     // SHA256 or content hash
}

public class ChunkData
{
    public required string Checksum { get; init; }
    public required byte[] Data { get; init; }
}
