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

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Text;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Serialization.Entities;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackageSerializer : ReleasePackageSerializerBase
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
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // Section: 0x02 - Chunk table
        await WriteSectionAsync(stream, 0x02, w =>
        {
            w.Write(ChecksumCompressor.TransposeCompress(package.Chunks.Select(x => x.Checksum).ToList())); // TODO: Improve performance by avoiding ToList
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Create the string table and tokenize components and files
        var substringBuilder = new SubstringTableBuilder();
        var tokenizedComponents = new List<(List<(ushort id, Separator sep)>, List<List<(ushort id, Separator sep)>>)>();
        var tokenizedProperties = new List<(List<(ushort id, Separator sep)>, List<(ushort id, Separator sep)> value)>();

        foreach (var comp in package.Components)
        {
            var compTokens = substringBuilder.Tokenize(comp.Name);
            var fileTokens = comp.Files.Select(f => substringBuilder.Tokenize(f.Name)).ToList();
            tokenizedComponents.Add((compTokens, fileTokens));
        }
        
        foreach (var kvp in package.CustomProperties)
        {
            var keyTokens = substringBuilder.Tokenize(kvp.Key);
            var valueTokens = substringBuilder.Tokenize(kvp.Value);
            tokenizedProperties.Add((keyTokens, valueTokens));
        }

        package.StringTable = substringBuilder.Table;
        
        // Section: 0x03 - String table
        await WriteSectionAsync(stream, 0x03, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)package.StringTable.Count);
            foreach (var s in package.StringTable)
            {
                // fast length without alloc
                var byteCount = Encoding.UTF8.GetByteCount(s);
                VarIntUtils.WriteVarInt(w, Convert.ToUInt32(byteCount));
                
                // encode into pooled buffer
                var rented = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    var written = Encoding.UTF8.GetBytes(s, 0, s.Length, rented, 0);
                    w.Write(rented, 0, written);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Section: 0x04 - Custom properties
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)package.CustomProperties.Count);
            for (var i = 0; i < package.CustomProperties.Count; i++)
            {
                var (keyTokens, valueTokens) = tokenizedProperties[i];
                WriteTokenSequence(w, keyTokens);
                WriteTokenSequence(w, valueTokens);
            }
        }, options.EnableCompression, options.CompressionLevel,  cancellationToken);
        
        // Generate the contentIds and fileContentId mappings
        var contentIds = GroupChunkListsByContentId(package);
        var fileContentIds = MapFilesToContentIds(package);
        
        // Filter and order deterministically (by contentId or first occurrence index)
        var dedupeList = new List<(ulong Cid, ChunkListStats Stats, int Occ, List<DeltaChunkRef> Chunks)>(contentIds.Count);
        foreach (var kv in contentIds)
            if (ShouldDeduplicate(kv.Value.Stats, kv.Value.Occurrences))
                dedupeList.Add((kv.Key, kv.Value.Stats, kv.Value.Occurrences, kv.Value.Chunks));

        dedupeList.Sort(static (a,b) => a.Cid.CompareTo(b.Cid)); // cheap and stable

        var contentIndexMap = new Dictionary<ulong, uint>(dedupeList.Count);
        for (var i = 0; i < dedupeList.Count; i++)
            contentIndexMap[dedupeList[i].Cid] = (uint)i;
        
        
        // Section: 0x05 - ContentId mapping
        await WriteSectionAsync(stream, 0x05, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)dedupeList.Count);
            
            foreach (var entry in dedupeList)
            {
                WriteChunkRefs(w, entry.Chunks);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Section: 0x06 - Components and files
        await WriteSectionAsync(stream, 0x06, w =>
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

                    var key = package.Components[i].Name + "\u001F" + file.Name;
                    var contentId = fileContentIds[key];
                    
                    if (dedupeList.Any(x => x.Cid == contentId))
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
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // Section: 0x07 - Package statistics
        await WriteSectionAsync(stream, 0x07, w =>
        {
            VarIntUtils.WriteVarInt(w , package.Stats.ComponentCount);
            VarIntUtils.WriteVarInt(w , package.Stats.FileCount);
            VarIntUtils.WriteVarInt(w, package.Stats.ChunkCount);
            VarIntUtils.WriteVarInt(w, package.Stats.RawSize);
            VarIntUtils.WriteVarInt(w, package.Stats.DedupedSize);
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
    }

    public static async Task<ReleasePackage> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(data);
        return await DeserializeAsync(stream, cancellationToken);
    }
    public static Task<ReleasePackage> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
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
            var s = (Stream)(isCompressed ? new DecompressionStream(compressed) : compressed);
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
                        var len = VarIntUtils.ReadVarInt<uint>(r); 
                        package.StringTable.Add(Encoding.UTF8.GetString(r.ReadBytes(Convert.ToInt32(len))));
                    }
                    break;
                case 0x04: // Section: 0x04 - Custom properties
                    var propCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < propCount; i++)
                    {
                        var key = ReadTokenizedString(r, package.StringTable);
                        var value = ReadTokenizedString(r, package.StringTable);
                        package.CustomProperties[key] = value;
                    }
                    break;
                case 0x05: // Section: 0x04 - ContentId mapping
                    var contentIdCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < contentIdCount; i++)
                    {
                        contentIds.Add(ReadChunkRefs(r));
                    }
                    break;
                case 0x06: // Section: 0x05 - Components and files
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
                case 0x07: // Section: 0x05 - Package statistics
                    package.Stats.ComponentCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Stats.FileCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Stats.ChunkCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Stats.RawSize = VarIntUtils.ReadVarInt<ulong>(r);
                    package.Stats.DedupedSize = VarIntUtils.ReadVarInt<ulong>(r);
                    break;
                
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }
        return Task.FromResult(package);
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
    
    private static ulong GetContentId(List<DeltaChunkRef> chunks)
    {
        // Generate a unique ID for the file based on its chunks
        var hasher = new XxHash3();
        Span<byte> buf = stackalloc byte[24]; // 3 x 8 bytes
        foreach (var c in chunks)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buf[..8], c.DeltaIndex);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(8, 8),  c.Offset);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(16, 8), c.Length);
            hasher.Append(buf);
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

                // Stable ID for the exact chunk list
                var contentId = GetContentId(chunks);

                if (contentIds.TryGetValue(contentId, out var existing))
                {
                    contentIds[contentId] = (existing.Chunks, existing.Stats, existing.Occurrences + 1);
                    continue;
                }

                byte bitsDelta, bitsOffset, bitsLength;
                byte[] packed;

                if (chunks.Count == 0)
                {
                    bitsDelta = bitsOffset = bitsLength = 0;
                    packed = [];
                }
                else
                {
                    static byte BitsU64(ulong max) => max == 0 ? (byte)0 : (byte)(BitOperations.Log2(max) + 1);

                    // Compute maxima (works whether DeltaIndex is uint or ulong)
                    var maxDelta  = chunks.Max(c => (ulong)c.DeltaIndex);
                    var maxOffset = chunks.Max(c => c.Offset);
                    var maxLength = chunks.Max(c => c.Length);

                    bitsDelta  = BitsU64(maxDelta);
                    bitsOffset = BitsU64(maxOffset);
                    bitsLength = BitsU64(maxLength);

                    var bw = new BitWriter();
                    foreach (var c in chunks)
                    {
                        if (bitsDelta  != 0) bw.WriteBits(c.DeltaIndex, bitsDelta);
                        if (bitsOffset != 0) bw.WriteBits(c.Offset, bitsOffset);
                        if (bitsLength != 0) bw.WriteBits(c.Length, bitsLength);
                    }

                    packed = bw.ToArray();
                }

                var stats = new ChunkListStats(bitsDelta, bitsOffset, bitsLength, packed);
                contentIds[contentId] = (chunks, stats, 1);
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
                result[comp.Name + "\u001F" + file.Name] = GetContentId(file.Chunks);
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
