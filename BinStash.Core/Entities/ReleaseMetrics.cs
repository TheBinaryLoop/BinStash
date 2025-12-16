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

namespace BinStash.Core.Entities;

public class ReleaseMetrics
{
    public Guid ReleaseId { get; set; }
    public Guid IngestSessionId { get; set; }
    public virtual IngestSession IngestSession { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int ChunksInRelease { get; set; }
    public int NewChunks { get; set; }
    public ulong TotalUncompressedSize { get; set; }
    public long NewCompressedBytes { get; set; }
    public int MetaBytesFull { get; set; }
    public int MetaBytesFullDiff { get; set; }
    public int ComponentsInRelease { get; set; }
    public int FilesInRelease { get; set; }
}