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

namespace BinStash.Core.Ingestion.Models;

public sealed class StorageWorkItem
{
    public required string Identity { get; init; }
    public required StorageWorkItemKind Kind { get; init; }
    
    // The output artifact this content belongs to.
    // For opaque files, this is the output file path itself.
    // For extracted members, this is the parent output artifact path (e.g. the archive file).
    public required string OutputArtifactPath { get; init; }
    
    public string? SourcePath {get; init; }
    public string? EntryPath { get; init; }
    public string? FormatId { get; init; }
    
    public long? LengthHint { get; init; }
    
    public required Func<Stream> OpenRead { get; init; }
}