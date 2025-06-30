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

[MessagePackObject(keyAsPropertyName: true)]
public class ReleasePackage
{
    public string Version { get; set; } = "1.0";
    public string ReleaseId { get; set; } = "";
    public string RepoId { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ChunkInfo> Chunks { get; set; } = new();
    public List<Component> Components { get; set; } = new();                 

    public ReleaseStats Stats { get; set; } = new();
}

[MessagePackObject(keyAsPropertyName: true)]
public class ChunkInfo
{
    public byte[] Checksum { get; set; } = [];
    public int Index { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class Component
{
    public string Name { get; set; } = "";
    public List<ReleaseFile> Files { get; set; } = new();
}


[MessagePackObject(keyAsPropertyName: true)]
public class ReleaseFile
{
    public string Name { get; set; } = "";
    public byte[] Hash { get; set; } = [];
    public string Component { get; set; } = "";

    public List<ChunkRef> Chunks { get; set; } = new();
}

[MessagePackObject(keyAsPropertyName: true)]
public class ChunkRef
{
    public int Index { get; set; }
    public long Offset { get; set; }
    public int Length { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class ReleaseStats
{
    public int FileCount { get; set; }
    public int ChunkCount { get; set; }
    public long UncompressedSize { get; set; }
    public long CompressedSize { get; set; }
}

/*public class ReleaseDefinition
{
    private const int TruncatedHashLength = 32;

    private Dictionary<string, int> ChunkMap = new(); // fullChecksum -> index
    private Dictionary<string, byte[]> ChecksumToTruncated = new();
    private Dictionary<string, List<(string fullChecksum, int index)>> CollisionMap = new(); // truncatedHex → list of full
    
    public ReleaseMeta Meta { get; set; }
    
    private List<ComponentDefinition> Components { get; set; } = new();

    public void AddChunk(string checksum)
    {
        if (ChunkMap.ContainsKey(checksum)) return;

        var fullBytes = Convert.FromHexString(checksum);
        var truncatedBytes = fullBytes[..TruncatedHashLength];
        var truncatedHex = Convert.ToHexString(truncatedBytes);

        var index = ChunkMap.Count > 0 ? ChunkMap.Values.Max() + 1 : 0;

        if (CollisionMap.TryGetValue(truncatedHex, out var existing))
        {
            // Collision already tracked
            existing.Add((checksum, index));
        }
        else
        {
            // Check if this truncated already used elsewhere
            if (ChecksumToTruncated.Values.Any(x => x.SequenceEqual(truncatedBytes)))
            {
                var prev = ChecksumToTruncated.First(kv => kv.Value.SequenceEqual(truncatedBytes));
                // Start new collision list
                CollisionMap[truncatedHex] =
                [
                    (prev.Key, ChunkMap[prev.Key]),
                    (checksum, index)
                ];

                // Remove the previous mapping, it's now in the collision bucket
                ChunkMap.Remove(prev.Key);
                ChecksumToTruncated.Remove(prev.Key);
            }
            else
            {
                ChecksumToTruncated[checksum] = truncatedBytes;
            }
        }

        ChunkMap[checksum] = index;
    }
    
    public void AddChunks(IEnumerable<string> checksums)
    {
        var existing = new HashSet<string>(ChunkMap.Keys);
        var newChecksums = checksums.Where(checksum => !existing.Contains(checksum)).ToList();

        var index = ChunkMap.Count > 0 ? ChunkMap.Values.Max() + 1 : 0;

        foreach (var checksum in newChecksums)
        {
            var fullBytes = Convert.FromHexString(checksum);
            var truncatedBytes = fullBytes[..TruncatedHashLength];
            var truncatedHex = Convert.ToHexString(truncatedBytes);

            if (CollisionMap.TryGetValue(truncatedHex, out var existingList))
            {
                existingList.Add((checksum, index));
            }
            else if (ChecksumToTruncated.Values.Any(x => x.SequenceEqual(truncatedBytes)))
            {
                var prev = ChecksumToTruncated.First(kv => kv.Value.SequenceEqual(truncatedBytes));
                CollisionMap[truncatedHex] =
                [
                    (prev.Key, ChunkMap[prev.Key]),
                    (checksum, index)
                ];
                ChunkMap.Remove(prev.Key);
                ChecksumToTruncated.Remove(prev.Key);
            }
            else
            {
                ChecksumToTruncated[checksum] = truncatedBytes;
            }

            ChunkMap[checksum] = index++;
        }
    }
    
    public List<string> GetUniqueChecksums()
    {
        return ChunkMap.Keys.ToList();
    }
    
    public ComponentDefinition AddComponent(string componentName)
    {
        var component = new ComponentDefinition
        {
            Name = componentName,
            Files = new List<FileDefinition>()
        };
        Components.Add(component);
        return component;
    }
    
    public List<ComponentDefinition> GetComponents() => Components;
    
    public async Task<byte[]> ToByteArrayAsync()
    {
        using var ms = new MemoryStream();
        await using var writer = new BinaryWriter(ms);

        // Write main chunk map (non-colliding entries)
        writer.Write7BitEncodedInt(ChecksumToTruncated.Count);
        foreach (var kvp in ChecksumToTruncated)
        {
            writer.Write(kvp.Value); // 12 bytes
            writer.Write7BitEncodedInt(ChunkMap[kvp.Key]);
        }

        // Write collision map
        writer.Write7BitEncodedInt(CollisionMap.Count);
        foreach (var kvp in CollisionMap)
        {
            var truncatedBytes = Convert.FromHexString(kvp.Key);
            writer.Write(truncatedBytes); // 12 bytes
            writer.Write7BitEncodedInt(kvp.Value.Count);
            foreach (var (fullChecksum, index) in kvp.Value)
            {
                writer.Write(Convert.FromHexString(fullChecksum)); // 32 bytes
                writer.Write7BitEncodedInt(index);
            }
        }

        // Write components (same as before, can delta-encode as above)
        writer.Write7BitEncodedInt(Components.Count);
        foreach (var component in Components)
        {
            writer.Write(component.Name);
            writer.Write7BitEncodedInt(component.Files.Count);

            foreach (var file in component.Files)
            {
                writer.Write(file.Name);
                writer.Write(file.Hash); // 8 bytes
                writer.Write7BitEncodedInt(file.ChunkEntries.Count);

                var prevIndex = 0;
                long prevOffset = 0;

                foreach (var chunk in file.ChunkEntries)
                {
                    var index = ChunkMap[chunk.Checksum];
                    writer.Write7BitEncodedInt(index - prevIndex);
                    writer.Write7BitEncodedInt((int)(chunk.Offset - prevOffset));
                    writer.Write7BitEncodedInt(chunk.Length);
                    prevIndex = index;
                    prevOffset = chunk.Offset;
                }
            }
        }

        return ms.ToArray();
    }
    
    public static ReleaseDefinition FromByteArray(byte[] data)
    {
        var result = new ReleaseDefinition();
        var indexToChecksum = new Dictionary<int, string>();

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Read main map
        var mainCount = reader.Read7BitEncodedInt();
        for (var i = 0; i < mainCount; i++)
        {
            var truncated = reader.ReadBytes(TruncatedHashLength);
            var index = reader.Read7BitEncodedInt();
            var hex = Convert.ToHexString(truncated);

            result.ChecksumToTruncated[hex] = truncated;
            indexToChecksum[index] = hex;
        }

        // Read collision map
        var collCount = reader.Read7BitEncodedInt();
        for (var i = 0; i < collCount; i++)
        {
            var truncated = reader.ReadBytes(TruncatedHashLength);
            var truncatedHex = Convert.ToHexString(truncated);

            var subCount = reader.Read7BitEncodedInt();
            var list = new List<(string fullChecksum, int index)>();

            for (var j = 0; j < subCount; j++)
            {
                var fullBytes = reader.ReadBytes(32);
                var full = Convert.ToHexString(fullBytes);
                var index = reader.Read7BitEncodedInt();
                list.Add((full, index));
                indexToChecksum[index] = full;
            }

            result.CollisionMap[truncatedHex] = list;
        }

        // Rebuild full chunk map
        foreach (var (index, checksum) in indexToChecksum)
            result.ChunkMap[checksum] = index;

        // Read components...
        var compCount = reader.Read7BitEncodedInt();
        for (var i = 0; i < compCount; i++)
        {
            var comp = new ComponentDefinition { Name = reader.ReadString() };
            var fileCount = reader.Read7BitEncodedInt();

            for (var j = 0; j < fileCount; j++)
            {
                var file = new FileDefinition { Name = reader.ReadString(), Hash = reader.ReadBytes(8) };
                var chunkCount = reader.Read7BitEncodedInt();

                var chunkIndex = 0;
                long offset = 0;

                for (var k = 0; k < chunkCount; k++)
                {
                    chunkIndex += reader.Read7BitEncodedInt();
                    offset += reader.Read7BitEncodedInt();
                    var length = reader.Read7BitEncodedInt();

                    file.AddChunkEntry(indexToChecksum[chunkIndex], offset, length);
                }

                comp.Files.Add(file);
            }

            result.Components.Add(comp);
        }

        return result;
    }

    public async IAsyncEnumerable<string> ToNdjsonAsync()
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        
        // Emit metadata
        var metaObj = new
        {
            type = "meta",
            version = Meta.Version,
            createdAt = Meta.CreatedAt.ToString("o"),
            createdBy = Meta.CreatedBy,
        };
        yield return JsonSerializer.Serialize(metaObj, options);
        await Task.Yield();
        
        // Emit components → files → chunks
        foreach (var component in Components)
        {

            foreach (var file in component.Files)
            {
                var fileObj = new
                {
                    type = "file",
                    component = component.Name,
                    name = file.Name,
                    hash = Convert.ToHexString(file.Hash),
                    chunkEntries = file.ChunkEntries.Select(ce => new
                    {
                        checksum = ce.Checksum,
                        offset = ce.Offset,
                        length = ce.Length
                    })
                };

                yield return JsonSerializer.Serialize(fileObj, options);
                await Task.Yield();
            }
        }
    }
    
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Release Definition:");
        sb.AppendLine($"Total Chunks: {ChunkMap.Count}");

        foreach (var component in Components)
        {
            sb.AppendLine($"Component: {component.Name}");

            // Build a directory tree from file paths
            var root = new DirectoryNode("");

            foreach (var file in component.Files)
            {
                var parts = file.Name.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                root.AddPath(parts);
            }

            PrintDirectoryTree(sb, root, 1);
        }

        return sb.ToString();
    }

    private class DirectoryNode
    {
        public string Name { get; }
        public Dictionary<string, DirectoryNode> Children { get; } = new();
        public bool IsFile { get; private set; }

        public DirectoryNode(string name)
        {
            Name = name;
        }

        public void AddPath(string[] parts, int index = 0)
        {
            if (index >= parts.Length)
                return;

            var part = parts[index];

            if (!Children.TryGetValue(part, out var child))
            {
                child = new DirectoryNode(part);
                Children[part] = child;
            }

            if (index == parts.Length - 1)
            {
                child.IsFile = true;
            }
            else
            {
                child.AddPath(parts, index + 1);
            }
        }
    }

    private void PrintDirectoryTree(StringBuilder sb, DirectoryNode node, int indentLevel)
    {
        foreach (var child in node.Children.Values.OrderBy(n => n.IsFile).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(' ', indentLevel * 2);
            sb.AppendLine($"- {child.Name}");
            if (!child.IsFile)
            {
                PrintDirectoryTree(sb, child, indentLevel + 1);
            }
        }
    }
}

public class ComponentDefinition
{
    public string Name { get; set; } = string.Empty;
    
    public List<FileDefinition> Files { get; set; } = new();
    
    public FileDefinition AddFile(string fileName, byte[] hash)
    {
        var file = new FileDefinition
        {
            Name = fileName,
            Hash = hash,
            ChunkEntries = new()
        };
        Files.Add(file);
        return file;
    }
}

public class FileDefinition
{
    public string Name { get; set; } = string.Empty;
    public byte[] Hash { get; set; } = [];
    public List<ChunkEntry> ChunkEntries { get; set; } = new();
    
    public void AddChunkEntry(string checksum, long offset, int length)
    {
        ChunkEntries.Add(new ChunkEntry
        {
            Checksum = checksum,
            Offset = offset,
            Length = length
        });
    }
}

public class ChunkEntry
{
    public required string Checksum { get; set; }
    public required long Offset { get; set; }
    public required int Length { get; set; }
}

public class ReleaseMeta
{
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class NdjsonStream : Stream
{
    private readonly IAsyncEnumerator<string> _lines;
    private readonly Encoding _encoding = Encoding.UTF8;
    private ReadOnlyMemory<byte> _currentBytes = ReadOnlyMemory<byte>.Empty;
    private bool _isCompleted;

    public NdjsonStream(ReleaseDefinition definition)
    {
        _lines = definition.ToNdjsonAsync().GetAsyncEnumerator();
    }

    public override async ValueTask DisposeAsync()
    {
        await _lines.DisposeAsync();
        await base.DisposeAsync();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_isCompleted) return 0;

        var dest = buffer.AsMemory(offset, count);

        int totalWritten = 0;
        while (totalWritten < count)
        {
            if (_currentBytes.IsEmpty)
            {
                if (!await _lines.MoveNextAsync())
                {
                    _isCompleted = true;
                    break;
                }

                var line = _lines.Current + "\n";
                _currentBytes = _encoding.GetBytes(line);
            }

            var toCopy = Math.Min(dest.Length - totalWritten, _currentBytes.Length);
            _currentBytes.Slice(0, toCopy).CopyTo(dest.Slice(totalWritten));
            totalWritten += toCopy;
            _currentBytes = _currentBytes.Slice(toCopy);
        }

        return totalWritten;
    }

    // Required overrides
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}*/