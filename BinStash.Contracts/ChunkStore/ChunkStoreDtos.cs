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

namespace BinStash.Contracts.ChunkStore;

public class ChunkStoreSummaryDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
}

public class ChunkStoreDetailDto : ChunkStoreSummaryDto
{
    public required string Type { get; set; }
    public required ChunkStoreChunkerDto Chunker { get; set; }
    public required Dictionary<string, object> Stats { get; set; }
}

public class ChunkStoreChunkerDto
{
    public string Type { get; set; } = string.Empty;
    public int? MinChunkSize { get; set; }
    public int? AvgChunkSize { get; set; }
    public int? MaxChunkSize { get; set; }
}

public class CreateChunkStoreDto
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string LocalPath { get; set; }
    public ChunkStoreChunkerDto? Chunker { get; set; }
}

public class ChunkStoreMissingChunkSyncInfoDto
{
    public required List<string> ChunkChecksums { get; set; }
}

public class ChunkUploadDto
{
    public required string Checksum { get; set; }
    public required byte[] Data { get; set; }
}