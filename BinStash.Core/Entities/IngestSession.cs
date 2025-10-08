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
//

namespace BinStash.Core.Entities;

public enum IngestSessionState
{
    Created = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Aborted = 4,
    Expired = 5
}

public class IngestSession
{
    public Guid Id { get; set; }
    public Guid RepoId { get; set; }
    public virtual Repository Repository { get; set; } = null!;
    public IngestSessionState State { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Chunk & Data stats
    public int ChunksSeenTotal { get; set; }
    public int ChunksSeenUnique { get; set; }
    public int ChunksSeenNew { get; set; }
    public long DataSizeTotal { get; set; }
    public long DataSizeUnique { get; set; }
    
    // File stats
    public int FilesSeenTotal { get; set; }
    public int FilesSeenUnique { get; set; }
    public int FilesSeenNew { get; set; }
    
    // Metadata stats
    public int MetadataSize { get; set; }
}