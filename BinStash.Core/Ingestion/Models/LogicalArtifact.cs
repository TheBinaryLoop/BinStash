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

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;

namespace BinStash.Core.Ingestion.Models;

public sealed class LogicalArtifact
{
    public required string LogicalPath { get; init; }
    public required string RelativePathWithinComponent { get; init; }
    public required Component Component { get; init; }
    public required ArtifactKind Kind { get; init; }

    public Hash32? FileHash { get; set; }
    public long? Length { get; set; }

    public string? SourcePath { get; set; }
    public string? FormatId { get; set; }
    public string? ParentLogicalPath { get; set; }
    
    // Optional container/member metadata
    public string? EntryPath { get; set; }
    public long? CompressedLength { get; set; }
    public bool IsVirtual { get; set; }
}