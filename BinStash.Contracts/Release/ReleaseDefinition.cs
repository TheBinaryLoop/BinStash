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

namespace BinStash.Contracts.Release;

public enum ListOp : byte { Keep = 0, Del = 1, Ins = 2 }

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
    public ulong Hash { get; set; }
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
    public Dictionary<string, string> CustomProperties { get; set; } = new();
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


public record PatchStringEntry(PatchOperation Op, ushort Id, string? Value);
public record PatchChunkEntry(PatchOperation Op, byte[] Hash);
public record PatchContentIdEntry(PatchOperation Op, ulong ContentId, List<DeltaChunkRef>? Chunks);
public record PatchFileEntry(PatchOperation Op, string Name, byte[]? Hash, List<DeltaChunkRef>? Chunks);
public record PatchComponentEntry(PatchOperation Op, string Name, List<PatchFileEntry>? Files);

public sealed class ComponentInsertPayload
{
    public string Name { get; set; } = string.Empty;
    public List<FileInsertPayload> Files { get; set; } = new(); // full files for new components
}

public sealed class FileInsertPayload
{
    public string Name { get; set; } = string.Empty;
    public ulong Hash { get; set; }
    public List<DeltaChunkRef> Chunks { get; set; } = new(); // DeltaIndex is CHILD after patch
}

// Per-component file script + insert payload
public sealed class FileListEdit
{
    public List<(ListOp Op, uint Len)> Runs { get; set; } = new();
    public List<FileInsertPayload> Insert { get; set; } = new();
    // Optional: separate “Modify”s for kept files that changed content:
    public List<(string Name, byte[]? Hash, List<DeltaChunkRef>? Chunks)> Modifies { get; set; } = new();
}

public class ReleasePackagePatch
{
    public string Version { get; set; } = "1.0";
    public string ReleaseId { get; set; } = string.Empty;
    public string RepoId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public int Level { get; set; } = 0;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    
    public List<PatchStringEntry> StringTableDelta { get; set; } = new();

    public List<(ListOp Op, uint Len)> ComponentRuns { get; set; } = new();
    public List<ComponentInsertPayload> ComponentInsert { get; set; } = new();
    
    // File list script per kept component (keyed by component name)
    public Dictionary<string, FileListEdit> FileEdits { get; set; } = new(StringComparer.Ordinal);
    
    public int ChunkFinalCount { get; set; }  
    public List<byte[]> ChunkInsertDict { get; set; } = new();
    public List<(byte Op, uint Len)> ChunkRuns { get; set; } = new(); // Op: 0=keep, 1=delete, 2=insert
    public List<PatchContentIdEntry> ContentIdDelta { get; set; } = new();
}

public enum PatchOperation : byte
{
    Add = 0x00,
    Remove = 0x01,
    Modify = 0x02
}