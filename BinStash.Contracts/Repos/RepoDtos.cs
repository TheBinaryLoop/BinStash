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

namespace BinStash.Contracts.Repos;

public class CreateRepositoryDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required Guid ChunkStoreId { get; set; }
}

public class RepositorySummaryDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required Guid ChunkStoreId { get; set; }
}

public class RepositoryConfigDto
{
    public required RepositoryDedupeConfigDto DedupeConfig { get; set; }
}

public class RepositoryDedupeConfigDto
{
    public required string Chunker { get; set; }
    public int? MinChunkSize { get; set; }
    public int? AvgChunkSize { get; set; }
    public int? MaxChunkSize { get; set; }
    public int? ShiftCount { get; set; }
    public int? BoundaryCheckBytes { get; set; }
}