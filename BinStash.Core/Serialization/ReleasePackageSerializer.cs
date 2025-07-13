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

using System.IO.Hashing;
using System.Text;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Serialization.Entities;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Serialization;

public static class ReleasePackageSerializer
{
    private const string Magic = "BPKG";
    private const byte Version = 1;
    // Flags
    private const byte CompressionFlag = 0b0000_0001;
    
    
    public static async Task<byte[]> SerializeAsync(ReleasePackage package, ReleasePackageSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await SerializeAsync(stream, package, options, cancellationToken);
        return stream.ToArray();
    }
    public static async Task SerializeAsync(Stream stream, ReleasePackage package, ReleasePackageSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ReleasePackageSerializerOptions.Default;
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        
        // Write magic, version and flags
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);
        
        byte flags = 0;
        if (options.EnableCompression)
            flags |= CompressionFlag;

        writer.Write(flags);

        // Section: 0x01 - Package metadata
        await WriteSectionAsync(stream, 0x01, w =>
        {
            w.Write(package.Version);
            w.Write(package.ReleaseId);
            w.Write(package.RepoId);
            w.Write(package.Notes ?? "");
            VarIntUtils.WriteVarInt(w, package.CreatedAt.ToUnixTimeSeconds());
        }, options, cancellationToken);

        // Section: 0x02 - Chunk table
        await WriteSectionAsync(stream, 0x02, w =>
        {
            w.Write(ChecksumCompressor.TransposeCompress(package.Chunks.Select(x => x.Checksum).ToList()));
        }, options, cancellationToken);
        
        // Create the string table and tokenize components and files
        var substringBuilder = new SubstringTableBuilder();
        var tokenizedComponents = new List<(List<(ushort id, Separator sep)>, List<List<(ushort id, Separator sep)>>)>();

        foreach (var comp in package.Components)
        {
            var compTokens = substringBuilder.Tokenize(comp.Name);
            var fileTokens = comp.Files.Select(f => substringBuilder.Tokenize(f.Name)).ToList();
            tokenizedComponents.Add((compTokens, fileTokens));
        }

        package.StringTable = substringBuilder.Table;
        
        // Section: 0x03 - String table
        await WriteSectionAsync(stream, 0x03, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)package.StringTable.Count);
            foreach (var s in package.StringTable)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                VarIntUtils.WriteVarInt(w, (ushort)bytes.Length);
                w.Write(bytes);
            }
        }, options, cancellationToken);
        
        // Generate the contentIds and fileContentId mappings
        var contentIds = GroupChunkListsByContentId(package);
        var fileContentIds = MapFilesToContentIds(package);
        var dedupeContentIds = contentIds
            .Where(kv => ShouldDeduplicate(kv.Value.Stats, kv.Value.Occurrences))
            .Select(kv => kv.Key)
            .ToHashSet();
        var contentIndexMap = dedupeContentIds
            .Select((cid, index) => new { cid, index })
            .ToDictionary(x => x.cid, x => (uint)x.index);
        
        // Section: 0x04 - ContentId mapping
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)dedupeContentIds.Count);
            
            foreach (var dedupeContentId in dedupeContentIds)
            {
                var (chunks, _, _) = contentIds[dedupeContentId];
                WriteChunkRefs(w, chunks);
            }
        }, options, cancellationToken);
        
        // Section: 0x05 - Components and files
        await WriteSectionAsync(stream, 0x05, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)tokenizedComponents.Count);
            for (var i = 0; i < tokenizedComponents.Count; i++)
            {
                var (compTokens, fileTokenLists) = tokenizedComponents[i];
                WriteTokenSequence(w, compTokens);
                VarIntUtils.WriteVarInt(w, (uint)fileTokenLists.Count);
                for (var j = 0; j < fileTokenLists.Count; j++)
                {
                    WriteTokenSequence(w, fileTokenLists[j]);

                    var file = package.Components[i].Files[j];
                    w.Write(file.Hash);

                    var contentId = fileContentIds[file.Name];
                    
                    if (dedupeContentIds.Contains(contentId))
                    {
                        // Reference using the content ID
                        w.Write((byte)0x01); // 0x01 = reference by content ID
                        VarIntUtils.WriteVarInt(w, contentIndexMap[contentId]);
                    }
                    else
                    {
                        // Inline using the known chunk list
                        w.Write((byte)0x00); // 0x00 = inline chunk list
                        WriteChunkRefs(w, file.Chunks);
                    }
                }
            }
        }, options, cancellationToken);

        // Section: 0x06 - Package statistics
        await WriteSectionAsync(stream, 0x06, w =>
        {
            VarIntUtils.WriteVarInt(w , package.Stats.FileCount);
            VarIntUtils.WriteVarInt(w, package.Stats.ChunkCount);
            VarIntUtils.WriteVarInt(w, package.Stats.UncompressedSize);
            VarIntUtils.WriteVarInt(w, package.Stats.CompressedSize);
        }, options, cancellationToken);
    }

    public static async Task<ReleasePackage> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(data);
        return await DeserializeAsync(stream, cancellationToken);
    }
    public static async Task<ReleasePackage> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        
        // Read magic, version and flags and validate
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != Magic) throw new InvalidDataException("Invalid magic");
        var version = reader.ReadByte();
        if (version != Version) throw new InvalidDataException("Unsupported version");
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;
        
        // Temporary lookups
        var contentIds = new List<List<DeltaChunkRef>>();

        var package = new ReleasePackage();
        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var sectionFlags = reader.ReadByte(); // Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var compressed = new MemoryStream(reader.ReadBytes((int)sectionSize));
            Stream s;
            if (isCompressed)
            {
                s = new DecompressionStream(compressed);
                /*await using var z = new DecompressionStream(compressed);
                await z.CopyToAsync(s, cancellationToken);
                s.Position = 0;*/
            }
            else
                s = compressed;
            
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    package.Version = r.ReadString();
                    package.ReleaseId = r.ReadString();
                    package.RepoId = r.ReadString();
                    package.Notes = r.ReadString();
                    package.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(VarIntUtils.ReadVarInt<long>(r));
                    break;
                case 0x02: // Section: 0x02 - Chunk table
                    package.Chunks.AddRange(ChecksumCompressor.TransposeDecompress(s).Select(x => new ChunkInfo(x)));
                    break;
                case 0x03: // Section: 0x03 - String table
                    var entryCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < entryCount; i++)
                    {
                        var len = VarIntUtils.ReadVarInt<ushort>(r);
                        package.StringTable.Add(Encoding.UTF8.GetString(r.ReadBytes(len)));
                    }
                    break;
                case 0x04: // Section: 0x04 - ContentId mapping
                    var contentIdCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < contentIdCount; i++)
                    {
                        contentIds.Add(ReadChunkRefs(r));
                    }
                    break;
                case 0x05: // Section: 0x05 - Components and files
                    var compCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedString(r, package.StringTable);
                        var comp = new Component { Name = compName };
                        var fileCount = VarIntUtils.ReadVarInt<uint>(r);
                        for (var j = 0; j < fileCount; j++)
                        {
                            var fileName = ReadTokenizedString(r, package.StringTable);
                            var file = new ReleaseFile
                            {
                                Name = fileName,
                                Hash = r.ReadBytes(8) // 8 bytes for XxHash3, maybe make this configurable?
                            };
                            
                            var chunkLocation = r.ReadByte();
                            switch (chunkLocation)
                            {
                                // Inline chunk list
                                case 0x00:
                                    file.Chunks = ReadChunkRefs(r);
                                    break;
                                // Reference by content ID
                                case 0x01:
                                {
                                    var contentIndex = VarIntUtils.ReadVarInt<uint>(r);
                                    file.Chunks = contentIds[(int)contentIndex];
                                    break;
                                }
                                default:
                                    throw new InvalidDataException($"Unknown chunk location type: {chunkLocation}");
                            }

                            comp.Files.Add(file);
                        }
                        package.Components.Add(comp);
                    }
                    break;
                case 0x06: // Section: 0x05 - Package statistics
                    package.Stats.FileCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Stats.ChunkCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Stats.UncompressedSize = VarIntUtils.ReadVarInt<ulong>(r);
                    package.Stats.CompressedSize = VarIntUtils.ReadVarInt<ulong>(r);
                    break;
            }
        }
        return package;
    }
    
    private static async Task WriteSectionAsync(Stream baseStream, byte id, Action<BinaryWriter> write, ReleasePackageSerializerOptions options, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        write(w);
        w.Flush();

        ms.Position = 0;
        
        if (options.EnableCompression)
        {
            using var compressed = new MemoryStream();
            await using (var z = new CompressionStream(compressed, new CompressionOptions(options.CompressionLevel)))
                await ms.CopyToAsync(z, ct);

            compressed.Position = 0;
            var writer = new BinaryWriter(baseStream, Encoding.UTF8, true);
            writer.Write(id);
            writer.Write((byte)0); // FLAG: Currently unused, reserved for future use
            VarIntUtils.WriteVarInt(writer, (ulong)compressed.Length);
            await compressed.CopyToAsync(baseStream, ct);
        }
        else
        {
            // Write uncompressed section data
            var writer = new BinaryWriter(baseStream, Encoding.UTF8, true);
            writer.Write(id);
            writer.Write((byte)0); // FLAG: Currently unused, reserved for future use
            VarIntUtils.WriteVarInt(writer, (ulong)ms.Length);
            await ms.CopyToAsync(baseStream, ct);
        }
    }
    
    private static void WriteTokenSequence(BinaryWriter w, List<(ushort id, Separator sep)> tokens)
    {
        VarIntUtils.WriteVarInt(w, (ushort)tokens.Count);
        foreach (var (id, sep) in tokens)
        {
            VarIntUtils.WriteVarInt(w, id);
            w.Write((byte)sep);
        }
    }
    
    private static string ReadTokenizedString(BinaryReader r, List<string> table)
    {
        var count = VarIntUtils.ReadVarInt<ushort>(r);
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            var id = VarIntUtils.ReadVarInt<ushort>(r);
            var sep = (Separator)r.ReadByte();
            sb.Append(table[id]);
            if (sep != Separator.None)
                sb.Append((char)sep);
        }

        return sb.ToString();
    }
    
    private static void WriteChunkRefs(BinaryWriter w, List<DeltaChunkRef> chunks)
    {
        VarIntUtils.WriteVarInt(w, (uint)chunks.Count);
        
        // Calculate bit widths for delta index, offset and length
        var bitsDelta = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.DeltaIndex) + 1));
        var bitsOffset = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.Offset) + 1));
        var bitsLength = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.Length) + 1));

        // Write bit widths
        w.Write(bitsDelta);
        w.Write(bitsOffset);
        w.Write(bitsLength);

        // Write bit-packed chunk data
        var bitWriter = new BitWriter();
        foreach (var chunk in chunks)
        {
            bitWriter.WriteBits(chunk.DeltaIndex, bitsDelta);
            bitWriter.WriteBits(chunk.Offset, bitsOffset);
            bitWriter.WriteBits(chunk.Length, bitsLength);
        }

        var packed = bitWriter.ToArray();
        VarIntUtils.WriteVarInt(w, (uint)packed.Length);
        w.Write(packed);
    }

    private static List<DeltaChunkRef> ReadChunkRefs(BinaryReader r)
    {
        var chunks = new List<DeltaChunkRef>();
        var chunkCount = VarIntUtils.ReadVarInt<uint>(r);
        
        var bitsDelta = r.ReadByte();
        var bitsOffset = r.ReadByte();
        var bitsLength = r.ReadByte();

        var packedLength = VarIntUtils.ReadVarInt<uint>(r);
        var packedData = r.ReadBytes((int)packedLength);

        var bitReader = new BitReader(packedData);
        for (var k = 0; k < chunkCount; k++)
        {
            var delta = (uint)bitReader.ReadBits(bitsDelta);
            var offset = bitReader.ReadBits(bitsOffset);
            var length = bitReader.ReadBits(bitsLength);
            chunks.Add(new DeltaChunkRef(delta, offset, length));
        }
        
        return chunks;
    }
    
    private static ulong GetContentId(List<DeltaChunkRef> chunks)
    {
        // Generate a unique ID for the file based on its chunks
        var hasher = new XxHash3();
        foreach (var chunk in chunks)
        {
            var buffer = new byte[24]; // 3 x 8 bytes for ulong
            BitConverter.TryWriteBytes(buffer.AsSpan(0, 8), chunk.DeltaIndex);
            BitConverter.TryWriteBytes(buffer.AsSpan(8, 8), chunk.Offset);
            BitConverter.TryWriteBytes(buffer.AsSpan(16, 8), chunk.Length);
            hasher.Append(buffer);
        }
        return hasher.GetCurrentHashAsUInt64();
    }
    
    private static Dictionary<ulong, (List<DeltaChunkRef> Chunks, ChunkListStats Stats, int Occurrences)> GroupChunkListsByContentId(ReleasePackage package)
    {
        var contentIds = new Dictionary<ulong, (List<DeltaChunkRef> Chunks, ChunkListStats Stats, int Occurrences)>();

        foreach (var comp in package.Components)
        {
            foreach (var file in comp.Files)
            {
                var chunks = file.Chunks;

                // Generate content ID
                var contentId = GetContentId(chunks);

                if (contentIds.TryGetValue(contentId, out var existing))
                {
                    contentIds[contentId] = (existing.Chunks, existing.Stats, existing.Occurrences + 1);
                }
                else
                {
                    var bitsDelta = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.DeltaIndex) + 1));
                    var bitsOffset = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.Offset) + 1));
                    var bitsLength = (byte)Math.Ceiling(Math.Log2(chunks.Max(c => c.Length) + 1));

                    var bitWriter = new BitWriter();
                    foreach (var chunk in chunks)
                    {
                        bitWriter.WriteBits(chunk.DeltaIndex, bitsDelta);
                        bitWriter.WriteBits(chunk.Offset, bitsOffset);
                        bitWriter.WriteBits(chunk.Length, bitsLength);
                    }

                    var packed = bitWriter.ToArray();
                    var stats = new ChunkListStats(bitsDelta, bitsOffset, bitsLength, packed);
                    contentIds[contentId] = (chunks, stats, 1);
                }
            }
        }

        return contentIds;
    }
    
    private static Dictionary<string, ulong> MapFilesToContentIds(ReleasePackage package)
    {
        var result = new Dictionary<string, ulong>();

        foreach (var comp in package.Components)
        {
            foreach (var file in comp.Files)
            {
                var contentId = GetContentId(file.Chunks);
                result[file.Name] = contentId;
            }
        }

        return result;
    }

    private static bool ShouldDeduplicate(ChunkListStats stats, int occurrences)
    {
        // Estimate inline size per file
        var chunkCount = EstimateChunkCount(stats.PackedBytes.Length, stats.BitsDelta, stats.BitsOffset, stats.BitsLength);
        var inlineSizePerFile =
            VarIntUtils.VarIntSize((uint)chunkCount) +
            3 + // bit widths
            VarIntUtils.VarIntSize((uint)stats.PackedBytes.Length) +
            stats.PackedBytes.Length;

        // Total size if inlined everywhere
        var totalInlineSize = (inlineSizePerFile + 1) * occurrences;

        // Deduplicated cost
        var sharedBlockSize =
            VarIntUtils.VarIntSize((uint)chunkCount) +
            3 + VarIntUtils.VarIntSize((uint)stats.PackedBytes.Length) +
            stats.PackedBytes.Length;
    
        var referenceCost = VarIntUtils.VarIntSize((uint)(occurrences - 1)); // ref per file (rough average)
        var totalDedupeSize = sharedBlockSize + (1 + referenceCost) * occurrences;
        
        return totalDedupeSize < totalInlineSize;
    }

    private static int EstimateChunkCount(int packedByteLength, byte bitsDelta, byte bitsOffset, byte bitsLength)
    {
        var totalBits = packedByteLength * 8;
        var bitsPerChunk = bitsDelta + bitsOffset + bitsLength;
        return totalBits / bitsPerChunk;
    }
}
