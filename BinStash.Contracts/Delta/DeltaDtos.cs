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

namespace BinStash.Contracts.Delta;

public record DeltaChunkRef(
    int Index,
    long Offset,
    string Checksum,
    int Length,
    string Source
);

public record DeltaFile(
    string Path,
    long Size,
    string OldHash,
    string NewHash,
    List<DeltaChunkRef> Chunks
);

public record DeltaManifest(
    string BaseReleaseId,
    string TargetReleaseId,
    List<DeltaFile> Files
);