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

using MessagePack;

namespace BinStash.Contracts.Release;

public readonly struct DeltaChunkRef
{
    public readonly uint DeltaIndex;
    public readonly ulong Offset;
    public readonly ulong Length;

    public DeltaChunkRef(uint deltaIndex, ulong offset, ulong length)
    {
        DeltaIndex = deltaIndex;
        Offset = offset;
        Length = length;
    }
}

public readonly struct ChunkInfo
{
    public readonly byte[] Checksum;
    public ChunkInfo(byte[] checksum) => Checksum = checksum;
}

public class ReleaseStats
{
    public uint ComponentCount { get; set; }
    public uint FileCount { get; set; }
    public uint ChunkCount { get; set; }
    public ulong RawSize { get; set; }
    public ulong DedupedSize { get; set; }
}

public class ReleaseFile
{
    public string Name { get; set; } = string.Empty;
    public byte[] Hash { get; set; } = [];
    public List<DeltaChunkRef> Chunks { get; set; } = [];
}

public class Component
{
    public string Name { get; set; } = string.Empty;
    public List<ReleaseFile> Files { get; set; } = [];
}

public class ReleasePackage
{
    public string Version { get; set; } = "1.0";
    public string ReleaseId { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ChunkInfo> Chunks { get; set; } = [];
    public List<string> StringTable { get; set; } = [];
    public List<Component> Components { get; set; } = [];
    public ReleaseStats Stats { get; set; } = new();
}

// Only used for file-rebuilding, not part of the actual release data
public class ChunkRef
{
    public int Index { get; set; }
    public long Offset { get; set; }
    public int Length { get; set; }
}