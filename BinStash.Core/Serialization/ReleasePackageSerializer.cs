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
using System.Collections;
using System.Text;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.IO;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackageSerializer : ReleasePackageSerializerBase
{
    private const string Magic = "BPKG";
    public static readonly byte Version = 2;
    // Flags
    private const byte CompressionFlag = 0b0000_0001;
    private const byte FileDefinitionLikedFlag = 0b0000_0010; // Only used for V1
    
    
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

        //var fileHashesMap = package.Components.SelectMany(x => x.Files).Select(f => f.Hash).Distinct().Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        
        // Count how often each file hash appears across all files
        var hashFreq = new Dictionary<Hash32, int>();
        foreach (var f in package.Components.SelectMany(c => c.Files))
        {
            if (hashFreq.TryGetValue(f.Hash, out var n)) hashFreq[f.Hash] = n + 1;
            else hashFreq[f.Hash] = 1;
        }

        // Order by descending frequency; tie-break deterministically by the hash value.
        var orderedHashes = hashFreq.Keys
            .OrderByDescending(h => hashFreq[h])
            .ThenBy(h => h)
            .ToList();

        // Map: Hash32 -> compact index
        var fileHashesMap = orderedHashes
            .Select((h, i) => (h, i))
            .ToDictionary(x => x.h, x => x.i);

        
        // Section: 0x02 - File hashes
        await WriteSectionAsync(stream, 0x02, w =>
        {
            w.Write(ChecksumCompressor.TransposeCompress(fileHashesMap.Select(x => x.Key.GetBytes()).ToList()));
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Create the string table and tokenize components and files
        var substringBuilder = new SubstringTableBuilder();
        var tokenizedComponents = new List<(List<(int id, Separator sep)>, List<List<(int id, Separator sep)>>)>();
        var tokenizedProperties = new List<(List<(int id, Separator sep)>, List<(int id, Separator sep)> value)>();

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
        
        // Build sortable records once to avoid re-encoding in the comparer
        var src = package.StringTable; // original order
        var stringTableEntryCount = src.Count;

        var records = new (int Orig, byte[] Bytes)[stringTableEntryCount];
        for (var i = 0; i < stringTableEntryCount; i++)
        {
            var b = Encoding.UTF8.GetBytes(src[i]);
            records[i] = (i, b);
        }

        // Sort by bytes (lexicographic, unsigned)
        Array.Sort(records, (a, b) =>
        {
            var x = a.Bytes;
            var y = b.Bytes;
            var len = Math.Min(x.Length, y.Length);
            for (var i = 0; i < len; i++)
            {
                var diff = x[i] - y[i];
                if (diff != 0) return diff;
            }
            return x.Length - y.Length;
        });

        // Build permutations
        var newIdOfOrig = new uint[stringTableEntryCount];
        for (uint newId = 0; newId < stringTableEntryCount; newId++)
        {
            var orig = records[newId].Orig;
            newIdOfOrig[orig] = newId;
        }
        
        // Section: 0x03 - String table
        await WriteSectionAsync(stream, 0x03, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)stringTableEntryCount);

            // 1) Write all lengths
            for (var i = 0; i < stringTableEntryCount; i++)
                VarIntUtils.WriteVarInt(w, (uint)records[i].Bytes.Length);

            // 2) Write the blob: all strings back-to-back
            for (var i = 0; i < stringTableEntryCount; i++)
                w.Write(records[i].Bytes, 0, records[i].Bytes.Length);
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        
        // Section: 0x04 - Custom properties
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)package.CustomProperties.Count);
            for (var i = 0; i < package.CustomProperties.Count; i++)
            {
                var (keyTokens, valueTokens) = tokenizedProperties[i];
                WriteTokenSequence(w, keyTokens, unsortedId => newIdOfOrig[unsortedId]);
                WriteTokenSequence(w, valueTokens, unsortedId => newIdOfOrig[unsortedId]);
            }
        }, options.EnableCompression, options.CompressionLevel,  cancellationToken);
        
        // Section: 0x05 - Components and files
        await WriteSectionAsync(stream, 0x05, w =>
        {
            //Console.WriteLine($"Components: {package.Components.Count}, Files: {package.Components.Sum(x => x.Files.Count)}");
            var compCount = tokenizedComponents.Count;
            VarIntUtils.WriteVarInt(w, (uint)compCount);

            for (var i = 0; i < compCount; i++)
            {
                var (compTokens, fileTokenLists) = tokenizedComponents[i];

                // Component name
                WriteTokenSequence(w, compTokens, unsortedId => newIdOfOrig[unsortedId]);

                // Files
                var fileCount = fileTokenLists.Count;
                VarIntUtils.WriteVarInt(w, (uint)fileCount);
                
                // Build (mappedTokens, file) pairs so we can sort and still access the correct file hash
                var entries = new List<(List<(uint id, Separator sep)> tokens, ReleaseFile file)>(fileCount);
                for (var j = 0; j < fileCount; j++)
                {
                    var mappedTokens = fileTokenLists[j]
                        .Select(t => (id: newIdOfOrig[checked((uint)t.id)], sep: t.sep))
                        .ToList();
                    entries.Add((mappedTokens, package.Components[i].Files[j]));
                }
                
                // Sort to maximize LCP
                entries.Sort((a, b) => CompareTokenLists(a.tokens, b.tokens));

                List<(uint id, Separator sep)>? prev = null;

                foreach (var (tokens, file) in entries)
                {
                    // Compute LCP with previous
                    uint lcp = 0;
                    if (prev != null)
                    {
                        var lim = Math.Min(prev.Count, tokens.Count);
                        while (lcp < lim && prev[(int)lcp].id == tokens[(int)lcp].id && prev[(int)lcp].sep == tokens[(int)lcp].sep)
                            lcp++;
                    }

                    var tailCount = (uint)tokens.Count - lcp;

                    // Emit LCP and tail count
                    VarIntUtils.WriteVarInt(w, lcp);
                    VarIntUtils.WriteVarInt(w, tailCount);

                    if (tailCount > 0)
                    {
                        // Tail IDs
                        for (var k = (int)lcp; k < tokens.Count; k++)
                            VarIntUtils.WriteVarInt(w, tokens[k].id);

                        // Tail seps -> wire codes -> nibble-pack
                        var tailSeps = new ReadOnlySpan<Separator>(tokens.Skip((int)lcp).Select(x => x.sep).ToArray());
                        var codes = new byte[tailSeps.Length];
                        for (var t = 0; t < tailSeps.Length; t++) codes[t] = EncodeSep(tailSeps[t]);
                        
                        var packedSeps = PackSeparatorsToNibbles(codes);
                        VarIntUtils.WriteVarInt(w, (uint)packedSeps.Length);
                        w.Write(packedSeps);
                    }

                    // File hash index
                    VarIntUtils.WriteVarInt(w, (uint)fileHashesMap[file.Hash]);

                    prev = tokens;
                }
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // Section: 0x06 - Package statistics
        await WriteSectionAsync(stream, 0x06, w =>
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
        return version switch
        {
            1 => DeserializeV1Async(reader, stream, cancellationToken),
            2 => DeserializeV2Async(reader, stream, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported version {version}")
        };
    }

    private static Task<ReleasePackage> DeserializeV1Async(BinaryReader reader, Stream stream, CancellationToken cancellationToken)
    {
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;
        var linkToFileDefinitions = (flags & FileDefinitionLikedFlag) != 0;
        
        Span<byte> fileHashBytesShort = stackalloc byte[8];
        Span<byte> fileHashBytesLong = stackalloc byte[32];
        
        // Temporary lookups
        var fileHashesMap = new Dictionary<int, Hash32>();
        var contentIds = new List<List<DeltaChunkRef>>();
        
        var package = new ReleasePackage();
        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var sectionFlags = reader.ReadByte(); // Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);
            //Console.WriteLine($"Section {sectionId:X2} ({sectionSize} bytes) at {stream.Position}");
            
            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed); 
            using var r = new BinaryReader(s);

            //Console.WriteLine($"Stream length: {s.Length}");
            //Console.WriteLine($"Current position: {s.Position:X8}");
            //Console.WriteLine($"Remaining size: {s.Length - s.Position}");
            
            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    ReadPackageMetadata(r, package);
                    break;
                case 0x02: // Section: 0x02 - Chunk table / File definitions
                    if (linkToFileDefinitions)
                    {
                        fileHashesMap = ChecksumCompressor.TransposeDecompress(s).Select((x, i) => (x, i)).ToDictionary(x => x.i, x => new Hash32(x.x));
                    }
                    else
                    {
                        using var nds = new NonDisposingStream(s);
                        package.Chunks.AddRange(ChecksumCompressor.TransposeDecompress(nds).Select(x => new ChunkInfo(x)));
                    }
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
                    package.CustomProperties = new Dictionary<string,string>(checked((int)propCount));
                    for (var i = 0; i < propCount; i++)
                    {
                        var key = ReadTokenizedStringV1(r, package.StringTable);
                        var value = ReadTokenizedStringV1(r, package.StringTable);
                        package.CustomProperties[key] = value;
                    }
                    break;
                case 0x05: // Section: 0x05 - ContentId mapping
                    var contentIdCount = VarIntUtils.ReadVarInt<uint>(r);
                    contentIds = new List<List<DeltaChunkRef>>(checked((int)contentIdCount));
                    for (var i = 0; i < contentIdCount; i++)
                        contentIds.Add(ReadChunkRefs(r));
                    break;
                case 0x06: // Section: 0x06 - Components and files
                    var compCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Components = new List<Component>(checked((int)compCount));
                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedStringV1(r, package.StringTable);
                        var comp = new Component { Name = compName };
                        var fileCount = VarIntUtils.ReadVarInt<uint>(r);
                        comp.Files = new List<ReleaseFile>(checked((int)fileCount));
                        for (var j = 0; j < fileCount; j++)
                        {
                            var fileName = ReadTokenizedStringV1(r, package.StringTable);
                            if (linkToFileDefinitions)
                            {
                                var fileHash = fileHashesMap[VarIntUtils.ReadVarInt<int>(r)];
                                var file = new ReleaseFile
                                {
                                    Name = fileName,
                                    Hash = fileHash,
                                };
                                comp.Files.Add(file);
                            }
                            else
                            {
                                r.BaseStream.ReadExactly(linkToFileDefinitions ? fileHashBytesLong : fileHashBytesShort);
                                var file = new ReleaseFile
                                {
                                    Name = fileName,
                                    Hash = new Hash32(fileHashBytesLong),
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
                                        file.Chunks = contentIds[checked((int)contentIndex)];
                                        break;
                                    }
                                    default:
                                        throw new InvalidDataException($"Unknown chunk location type: {chunkLocation}");
                                }

                                comp.Files.Add(file);
                            }
                        }
                        package.Components.Add(comp);
                    }
                    break;
                case 0x07: // Section: 0x07 - Package statistics
                    ReadPackageStats(r, package);
                    break;
                
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }
        
        return Task.FromResult(package);
    }

    private static Task<ReleasePackage> DeserializeV2Async(BinaryReader reader, Stream stream, CancellationToken cancellationToken)
    {
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;
        
        // Temporary lookups
        var fileHashesMap = new Dictionary<int, Hash32>();
        
        var package = new ReleasePackage();
        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var sectionFlags = reader.ReadByte(); // Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed);
            using var r = new BinaryReader(s);
            
            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    ReadPackageMetadata(r, package);
                    break;
                case 0x02: // Section: 0x02 - File definitions
                    fileHashesMap = ChecksumCompressor.TransposeDecompress(s).Select((x, i) => (x, i)).ToDictionary(x => x.i, x => new Hash32(x.x));
                    break;
                case 0x03: // Section: 0x03 - String table
                    // Layout: [count] [len1..lenN] [bytes for s1..sN]
                    var entryCount = checked((int)VarIntUtils.ReadVarInt<uint>(r));

                    // 1) Read all lengths
                    var lengths = new int[entryCount];
                    var maxLen = 0;
                    for (var i = 0; i < entryCount; i++)
                    {
                        var len = (int)VarIntUtils.ReadVarInt<uint>(r);
                        lengths[i] = len;
                        if (len > maxLen) maxLen = len;
                    }
                    
                    // 2) Read and decode strings using one pooled buffer
                    var buffer = ArrayPool<byte>.Shared.Rent(maxLen);
                    try
                    {
                        for (var i = 0; i < entryCount; i++)
                        {
                            var len = lengths[i];
                            r.BaseStream.ReadExactly(buffer, 0, len);
                            package.StringTable.Add(Encoding.UTF8.GetString(buffer, 0, len));
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                    break;
                case 0x04: // Section: 0x04 - Custom properties
                    ReadCustomProperties(r, package);
                    break;
                case 0x05: // Section: 0x05 - Components and files
                    var compCount = VarIntUtils.ReadVarInt<uint>(r);
                    package.Components = new List<Component>(checked((int)compCount));

                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedString(r, package.StringTable);
                        var comp = new Component { Name = compName };

                        var fileCount = VarIntUtils.ReadVarInt<uint>(r);
                        comp.Files = new List<ReleaseFile>(checked((int)fileCount));

                        List<(uint id, Separator sep)>? prev = null;

                        for (var j = 0; j < fileCount; j++)
                        {
                            var lcp  = VarIntUtils.ReadVarInt<uint>(r);
                            var tail = VarIntUtils.ReadVarInt<uint>(r);

                            List<(uint id, Separator sep)> tokens;
                            if (prev is null)
                            {
                                if (lcp != 0)
                                    throw new InvalidDataException("First file in component must have LCP=0.");
                                tokens = new List<(uint id, Separator sep)>(checked((int)tail));
                            }
                            else
                            {
                                if (lcp > prev.Count)
                                    throw new InvalidDataException("LCP exceeds previous token count.");
                                tokens = new List<(uint id, Separator sep)>(checked((int)(lcp + tail)));
                                // copy prefix
                                for (var k = 0; k < lcp; k++)
                                    tokens.Add(prev[k]);
                            }

                            if (tail > 0)
                            {
                                // Read tail IDs
                                var tailIds = new uint[tail];
                                for (var k = 0; k < tail; k++)
                                    tailIds[k] = VarIntUtils.ReadVarInt<uint>(r);

                                // Read packed separators
                                var packedLen = (int)VarIntUtils.ReadVarInt<uint>(r);
                                if (packedLen < ((tail + 1) >> 1))
                                    throw new InvalidDataException("Separator stream too short.");

                                var packed = r.ReadBytes(packedLen);
                                if (packed.Length != packedLen)
                                    throw new EndOfStreamException("EOF in packed separator stream.");

                                // Unpack
                                var codes = new byte[tail];
                                UnpackSeparatorsFromNibbles(codes, packed);
                                for (var k = 0; k < tail; k++)
                                    tokens.Add((tailIds[k], DecodeSep(codes[k])));
                            }

                            // Build string
                            var fileName = BuildStringFromTokens(tokens, package.StringTable);

                            // Hash index
                            var fileHash = fileHashesMap[checked((int)VarIntUtils.ReadVarInt<uint>(r))];

                            comp.Files.Add(new ReleaseFile
                            {
                                Name = fileName,
                                Hash = fileHash
                            });

                            prev = tokens;
                        }

                        package.Components.Add(comp);
                    }
                    break;
                case 0x06: // Section: 0x06 - Package statistics
                    ReadPackageStats(r, package);
                    break;
                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }
        
        return Task.FromResult(package);
    }

    private static Stream GetSectionStream(byte sectionId, Stream stream, uint sectionSize, bool isCompressed)
    {
        var slice = new BoundedStream(stream, (int)sectionSize);
        var decompOptions = ZstDicts.TryGetValue(sectionId, out var dict) ? new DecompressionOptions(dict) : null;
        var s = (Stream)(isCompressed ? new DecompressionStream(slice, decompOptions) : slice);
        return s;
    }

    private static void ReadPackageMetadata(BinaryReader r, ReleasePackage package)
    {
        package.Version = r.ReadString();
        package.ReleaseId = r.ReadString();
        package.RepoId = r.ReadString();
        package.Notes = r.ReadString();
        package.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(VarIntUtils.ReadVarInt<long>(r));
    }

    private static void ReadCustomProperties(BinaryReader r, ReleasePackage package)
    {
        var propCount = VarIntUtils.ReadVarInt<uint>(r);
        package.CustomProperties = new Dictionary<string,string>(checked((int)propCount));
        for (var i = 0; i < propCount; i++)
        {
            var key = ReadTokenizedString(r, package.StringTable);
            var value = ReadTokenizedString(r, package.StringTable);
            package.CustomProperties[key] = value;
        }
    }

    private static void ReadPackageStats(BinaryReader r, ReleasePackage package)
    {
        package.Stats.ComponentCount = VarIntUtils.ReadVarInt<uint>(r);
        package.Stats.FileCount = VarIntUtils.ReadVarInt<uint>(r);
        package.Stats.ChunkCount = VarIntUtils.ReadVarInt<uint>(r);
        package.Stats.RawSize = VarIntUtils.ReadVarInt<ulong>(r);
        package.Stats.DedupedSize = VarIntUtils.ReadVarInt<ulong>(r);
    }
    
    private static void WriteTokenSequence(BinaryWriter w, List<(int id, Separator sep)> tokens, Func<uint, uint> mapId)
    {
        var mapped = tokens.Select(t => (mapId(checked((uint)t.id)), t.sep)).ToList();
        WriteTokenSequence(w, mapped);
    }
    private static void WriteTokenSequence(BinaryWriter w, List<(uint id, Separator sep)> tokens)
    {
        VarIntUtils.WriteVarInt(w, tokens.Count);
        foreach (var (id, sep) in tokens)
        {
            VarIntUtils.WriteVarInt(w, id);
            w.Write((byte)sep);
        }
    }
    
    private static string ReadTokenizedStringV1(BinaryReader r, List<string> table)
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
    
    private static string ReadTokenizedString(BinaryReader r, List<string> table)
    {
        var count = VarIntUtils.ReadVarInt<int>(r);
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            var id = VarIntUtils.ReadVarInt<uint>(r);
            var sep = (Separator)r.ReadByte();
            sb.Append(table[checked((int)id)]);
            if (sep != Separator.None)
                sb.Append((char)sep);
        }

        return sb.ToString();
    }
    
    private static byte[] PackSeparatorsToNibbles(ReadOnlySpan<byte> codes)
    {
        var len = (codes.Length + 1) >> 1;
        var buf = new byte[len];
        var bi = 0;
        for (var i = 0; i < codes.Length; i += 2)
        {
            var hi = (byte)(codes[i] & 0x0F);
            var b = (byte)(hi << 4);
            if (i + 1 < codes.Length)
            {
                var lo = (byte)(codes[i + 1] & 0x0F);
                b |= lo;
            }
            buf[bi++] = b;
        }
        return buf;
    }

    private static void UnpackSeparatorsFromNibbles(Span<byte> dest, ReadOnlySpan<byte> packed)
    {
        var di = 0;
        foreach (var b in packed)
        {
            if (di < dest.Length) dest[di++] = (byte)((b >> 4) & 0x0F);
            if (di < dest.Length) dest[di++] = (byte)(b & 0x0F);
        }
    }
    
    private static string BuildStringFromTokens(List<(uint id, Separator sep)> tokens, List<string> table)
    {
        var sb = new StringBuilder();
        foreach (var (id, sep) in tokens)
        {
            sb.Append(table[checked((int)id)]);
            if (sep != Separator.None)
                sb.Append((char)sep);
        }
        return sb.ToString();
    }
    
    private static int CompareTokenLists(List<(uint id, Separator sep)> a, List<(uint id, Separator sep)> b)
    {
        var n = Math.Min(a.Count, b.Count);
        for (var i = 0; i < n; i++)
        {
            var c = a[i].id.CompareTo(b[i].id);
            if (c != 0) return c;
            // Use wire code order for separators to keep it consistent
            c = EncodeSep(a[i].sep).CompareTo(EncodeSep(b[i].sep));
            if (c != 0) return c;
        }
        return a.Count.CompareTo(b.Count);
    }
    
    private static byte EncodeSep(Separator sep) => sep switch
    {
        Separator.None => 0,
        Separator.Dot => 1,
        Separator.Slash => 2,
        Separator.Backslash => 3,
        Separator.Colon => 4,
        Separator.Dash => 5,
        Separator.Underscore => 6,
        _ => throw new InvalidDataException($"Separator not encodable: {sep}")
    };

    private static Separator DecodeSep(byte code) => code switch
    {
        0 => Separator.None,
        1 => Separator.Dot,
        2 => Separator.Slash,
        3 => Separator.Backslash,
        4 => Separator.Colon,
        5 => Separator.Dash,
        6 => Separator.Underscore,
        _ => throw new InvalidDataException($"Unknown separator wire code: {code}")
    };

}
