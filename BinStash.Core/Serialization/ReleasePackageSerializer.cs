// Copyright (C) 2025-2026  Lukas Eßmann
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
using System.Text;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.Helper;
using BinStash.Core.IO;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackageSerializer : ReleasePackageSerializerBase
{
    private const string Magic = "BPKG";
    public static readonly byte Version = 4;
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

        // Header
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);

        byte flags = 0;
        if (options.EnableCompression)
            flags |= CompressionFlag;

        writer.Write(flags);

        // Build tables
        var contentHashes = CollectContentHashes(package);
        var contentHashIndex = contentHashes
            .Select((h, i) => (h, i))
            .ToDictionary(x => x.h, x => x.i);

        // string table stores path segments (tokens), not full paths
        var (tokenTable, tokenIndex) = BuildTokenTable(package);

        var opaqueBackings = new List<OpaqueBlobBacking>();
        var reconstructedBackings = new List<ReconstructedContainerBacking>();
        var outputArtifactRecords = new List<OutputArtifactRecord>();

        // Sort artifacts by path before serialisation.
        // Adjacent artifacts share path-token prefix runs, which significantly
        // improves Zstd compression of §0x05 and §0x06 (~17 KB saving on a
        // ~11k-artifact sample vs insertion order).
        var sortedArtifacts = package.OutputArtifacts
            .OrderBy(a => a.Path, StringComparer.Ordinal)
            .ToList();

        foreach (var artifact in sortedArtifacts)
        {
            var pathTokens = SplitPathToTokens(artifact.Path);
            var pathTokenIndices = pathTokens.Select(t => tokenIndex[t]).ToArray();

            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    outputArtifactRecords.Add(new OutputArtifactRecord
                    {
                        PathTokenIndices = pathTokenIndices,
                        Kind = artifact.Kind,
                        RequiresBytePerfectReconstruction = artifact.RequiresBytePerfectReconstruction,
                        BackingType = BackingType.OpaqueBlob,
                        BackingIndex = opaqueBackings.Count
                    });
                    opaqueBackings.Add(opaque);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    outputArtifactRecords.Add(new OutputArtifactRecord
                    {
                        PathTokenIndices = pathTokenIndices,
                        Kind = artifact.Kind,
                        RequiresBytePerfectReconstruction = artifact.RequiresBytePerfectReconstruction,
                        BackingType = BackingType.ReconstructedContainer,
                        BackingIndex = reconstructedBackings.Count
                    });
                    reconstructedBackings.Add(reconstructed);
                    break;

                default:
                    throw new NotSupportedException($"Unsupported artifact backing type: {artifact.Backing.GetType().FullName}");
            }
        }

        var flattenedMembers = new List<V4ContainerMemberRecord>();
        var reconstructedBackingRecords = new List<ReconstructedBackingRecord>();
        var recipePayloads = new List<byte[]>();

        foreach (var backing in reconstructedBackings)
        {
            var memberStart = flattenedMembers.Count;

            foreach (var member in backing.Members)
            {
                if (member.ContentHash == null)
                    throw new InvalidDataException($"Container member '{member.EntryPath}' is missing ContentHash.");
                if (member.Length == null)
                    throw new InvalidDataException($"Container member '{member.EntryPath}' is missing Length.");

                var memberPathTokens = SplitPathToTokens(member.EntryPath);
                flattenedMembers.Add(new V4ContainerMemberRecord
                {
                    EntryPathTokenIndices = memberPathTokens.Select(t => tokenIndex[t]).ToArray(),
                    ContentHashIndex = contentHashIndex[member.ContentHash.Value],
                    Length = member.Length.Value
                });
            }

            var memberCount = flattenedMembers.Count - memberStart;
            var recipeIndex = recipePayloads.Count;
            recipePayloads.Add(backing.RecipePayload);

            reconstructedBackingRecords.Add(new ReconstructedBackingRecord
            {
                FormatIdIndex = tokenIndex[backing.FormatId],
                ReconstructionKind = backing.ReconstructionKind,
                MemberStart = memberStart,
                MemberCount = memberCount,
                RecipePayloadIndex = recipeIndex
            });
        }

        // 0x01 - metadata
        await WriteSectionAsync(stream, 0x01, w =>
        {
            w.Write(package.Version);
            w.Write(package.ReleaseId);
            w.Write(package.RepoId);
            w.Write(package.Notes ?? "");
            VarIntUtils.WriteVarInt(w, package.CreatedAt.ToUnixTimeSeconds());
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x02 - content hashes
        await WriteSectionAsync(stream, 0x02, w =>
        {
            w.Write(ChecksumCompressor.TransposeCompress(contentHashes.Select(x => x.GetBytes()).ToList()));
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x03 - token table (V4: path segments, not full paths)
        await WriteSectionAsync(stream, 0x03, w =>
        {
            WriteStringTable(w, tokenTable);
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x04 - custom properties
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)package.CustomProperties.Count);
            foreach (var kvp in package.CustomProperties)
            {
                // Custom property keys/values are stored verbatim as single tokens
                VarIntUtils.WriteVarInt(w, (uint)tokenIndex[kvp.Key]);
                VarIntUtils.WriteVarInt(w, (uint)tokenIndex[kvp.Value]);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x05 - output artifacts (V4: path as token sequence, no ComponentNameIndex, no BackingIndex)
        // BackingIndex is implicit: the k-th artifact with BackingType=OpaqueBlob maps to
        // the k-th entry in §0x06; similarly for ReconstructedContainer → §0x07.
        await WriteSectionAsync(stream, 0x05, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)outputArtifactRecords.Count);
            foreach (var record in outputArtifactRecords)
            {
                VarIntUtils.WriteVarInt(w, (uint)record.PathTokenIndices.Length);
                foreach (var idx in record.PathTokenIndices)
                    VarIntUtils.WriteVarInt(w, (uint)idx);
                w.Write((byte)record.Kind);
                w.Write(record.RequiresBytePerfectReconstruction ? (byte)1 : (byte)0);
                w.Write((byte)record.BackingType);
                // BackingIndex is NOT written: inferred from position during deserialization
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x06 - opaque backings
        await WriteSectionAsync(stream, 0x06, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)opaqueBackings.Count);
            foreach (var backing in opaqueBackings)
            {
                if (backing.ContentHash == null)
                    throw new InvalidDataException("OpaqueBlobBacking.ContentHash must be set before serialization.");
                if (backing.Length == null)
                    throw new InvalidDataException("OpaqueBlobBacking.Length must be set before serialization.");

                VarIntUtils.WriteVarInt(w, (uint)contentHashIndex[backing.ContentHash.Value]);
                VarIntUtils.WriteVarInt(w, (ulong)backing.Length.Value);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x07 - reconstructed backings
        await WriteSectionAsync(stream, 0x07, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)reconstructedBackingRecords.Count);
            foreach (var record in reconstructedBackingRecords)
            {
                VarIntUtils.WriteVarInt(w, (uint)record.FormatIdIndex);
                w.Write((byte)record.ReconstructionKind);
                VarIntUtils.WriteVarInt(w, (uint)record.MemberStart);
                VarIntUtils.WriteVarInt(w, (uint)record.MemberCount);
                VarIntUtils.WriteVarInt(w, (uint)record.RecipePayloadIndex);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x08 - container members (entry path as token sequence)
        await WriteSectionAsync(stream, 0x08, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)flattenedMembers.Count);
            foreach (var member in flattenedMembers)
            {
                VarIntUtils.WriteVarInt(w, (uint)member.EntryPathTokenIndices.Length);
                foreach (var idx in member.EntryPathTokenIndices)
                    VarIntUtils.WriteVarInt(w, (uint)idx);
                VarIntUtils.WriteVarInt(w, (uint)member.ContentHashIndex);
                VarIntUtils.WriteVarInt(w, (ulong)member.Length);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x09 - recipe payloads
        await WriteSectionAsync(stream, 0x09, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)recipePayloads.Count);
            foreach (var payload in recipePayloads)
            {
                VarIntUtils.WriteVarInt(w, (uint)payload.Length);
                w.Write(payload);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x0A - stats
        await WriteSectionAsync(stream, 0x0A, w =>
        {
            VarIntUtils.WriteVarInt(w, package.Stats.ComponentCount);
            VarIntUtils.WriteVarInt(w, package.Stats.FileCount);
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
            3 => DeserializeV3Async(reader, stream, cancellationToken),
            4 => DeserializeV4Async(reader, stream, cancellationToken),
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
            _ = reader.ReadByte(); // sectionFlags: Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);
            
            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed); 
            using var r = new BinaryReader(s);
            
            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    ReadPackageMetadata(r, package);
                    break;
                case 0x02: // Section: 0x02 - Chunk table / File definitions
                    if (linkToFileDefinitions)
                        fileHashesMap = ChecksumCompressor.TransposeDecompressHashes(s).Select((x, i) => (x, i)).ToDictionary(x => x.i, x => x.x);
                    else
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

                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedStringV1(r, package.StringTable);
                        var fileCount = VarIntUtils.ReadVarInt<uint>(r);

                        for (var j = 0; j < fileCount; j++)
                        {
                            var fileName = ReadTokenizedStringV1(r, package.StringTable);
                            Hash32 fileHash;

                            if (linkToFileDefinitions)
                            {
                                fileHash = fileHashesMap[VarIntUtils.ReadVarInt<int>(r)];
                            }
                            else
                            {
                                r.BaseStream.ReadExactly(fileHashBytesLong);
                                fileHash = new Hash32(fileHashBytesLong);

                                var chunkLocation = r.ReadByte();
                                switch (chunkLocation)
                                {
                                    case 0x00:
                                        _ = ReadChunkRefs(r);
                                        break;
                                    case 0x01:
                                        _ = contentIds[checked((int)VarIntUtils.ReadVarInt<uint>(r))];
                                        break;
                                    default:
                                        throw new InvalidDataException($"Unknown chunk location type: {chunkLocation}");
                                }
                            }

                            var outputPath = CombineComponentAndFilePath(compName, fileName);

                            package.OutputArtifacts.Add(new OutputArtifact
                            {
                                Path = outputPath,
                                ComponentName = compName,
                                Kind = OutputArtifactKind.File,
                                RequiresBytePerfectReconstruction = true,
                                Backing = new OpaqueBlobBacking
                                {
                                    ContentHash = fileHash,
                                    Length = null
                                }
                            });
                        }
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
            _ = reader.ReadByte(); // sectionFlags: Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed);
            using var r = new BinaryReader(s);
            
            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    ReadPackageMetadata(r, package);
                    break;
                case 0x02: // Section: 0x02 - File definitions
                    fileHashesMap = ChecksumCompressor.TransposeDecompressHashes(s).Select((x, i) => (x, i)).ToDictionary(x => x.i, x => x.x);
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

                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedString(r, package.StringTable);
                        var fileCount = VarIntUtils.ReadVarInt<uint>(r);

                        List<(uint id, Separator sep)>? prev = null;

                        for (var j = 0; j < fileCount; j++)
                        {
                            var lcp = VarIntUtils.ReadVarInt<uint>(r);
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
                                for (var k = 0; k < lcp; k++)
                                    tokens.Add(prev[k]);
                            }

                            if (tail > 0)
                            {
                                var tailIds = new uint[tail];
                                for (var k = 0; k < tail; k++)
                                    tailIds[k] = VarIntUtils.ReadVarInt<uint>(r);

                                var packedLen = checked((int)VarIntUtils.ReadVarInt<uint>(r));
                                if (packedLen < ((tail + 1) >> 1))
                                    throw new InvalidDataException("Separator stream too short.");

                                var packed = r.ReadBytes(packedLen);
                                if (packed.Length != packedLen)
                                    throw new EndOfStreamException("EOF in packed separator stream.");

                                var codes = new byte[tail];
                                UnpackSeparatorsFromNibbles(codes, packed);
                                for (var k = 0; k < tail; k++)
                                    tokens.Add((tailIds[k], DecodeSep(codes[k])));
                            }

                            var fileName = BuildStringFromTokens(tokens, package.StringTable);
                            var fileHash = fileHashesMap[checked((int)VarIntUtils.ReadVarInt<uint>(r))];

                            var outputPath = CombineComponentAndFilePath(compName, fileName);

                            package.OutputArtifacts.Add(new OutputArtifact
                            {
                                Path = outputPath,
                                ComponentName = compName,
                                Kind = OutputArtifactKind.File,
                                RequiresBytePerfectReconstruction = true,
                                Backing = new OpaqueBlobBacking
                                {
                                    ContentHash = fileHash,
                                    Length = null
                                }
                            });

                            prev = tokens;
                        }
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
    
    private static Task<ReleasePackage> DeserializeV3Async(BinaryReader reader, Stream stream, CancellationToken cancellationToken)
    {
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;

        var contentHashes = new List<Hash32>();
        var stringTable = new List<string>();
        var customProperties = new Dictionary<string, string>(StringComparer.Ordinal);

        var outputArtifactTemps = new List<V3OutputArtifactTemp>();
        var opaqueBackings = new List<V3OpaqueBackingTemp>();
        var reconstructedBackings = new List<V3ReconstructedBackingTemp>();
        var memberTemps = new List<V3ContainerMemberTemp>();
        var recipePayloads = new List<byte[]>();

        var package = new ReleasePackage();

        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            _ = reader.ReadByte(); // sectionFlags: Reserved for future usage
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed);
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // metadata
                    ReadPackageMetadata(r, package);
                    break;

                case 0x02: // content hashes
                    contentHashes = ChecksumCompressor.TransposeDecompressHashes(s).ToList();
                    break;

                case 0x03: // string table
                    stringTable = ReadStringTable(r);
                    package.StringTable = stringTable;
                    break;

                case 0x04: // custom properties
                {
                    var propCount = VarIntUtils.ReadVarInt<uint>(r);
                    customProperties = new Dictionary<string, string>(checked((int)propCount), StringComparer.Ordinal);
                    for (var i = 0; i < propCount; i++)
                    {
                        var keyIndex = VarIntUtils.ReadVarInt<uint>(r);
                        var valueIndex = VarIntUtils.ReadVarInt<uint>(r);
                        customProperties[stringTable[checked((int)keyIndex)]] = stringTable[checked((int)valueIndex)];
                    }
                    break;
                }

                case 0x05: // output artifacts
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    outputArtifactTemps = new List<V3OutputArtifactTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        outputArtifactTemps.Add(new V3OutputArtifactTemp
                        {
                            PathIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            ComponentNameIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            Kind = (OutputArtifactKind)r.ReadByte(),
                            RequiresBytePerfectReconstruction = r.ReadByte() != 0,
                            BackingType = (BackingType)r.ReadByte(),
                            BackingIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r))
                        });
                    }

                    break;
                }

                case 0x06: // opaque backings
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    opaqueBackings = new List<V3OpaqueBackingTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        opaqueBackings.Add(new V3OpaqueBackingTemp
                        {
                            ContentHashIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            Length = checked((long)VarIntUtils.ReadVarInt<ulong>(r))
                        });
                    }

                    break;
                }

                case 0x07: // reconstructed backings
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    reconstructedBackings = new List<V3ReconstructedBackingTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        reconstructedBackings.Add(new V3ReconstructedBackingTemp
                        {
                            FormatIdIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            ReconstructionKind = (ReconstructionKind)r.ReadByte(),
                            MemberStart = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            MemberCount = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            RecipePayloadIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r))
                        });
                    }

                    break;
                }

                case 0x08: // container members
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    memberTemps = new List<V3ContainerMemberTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        memberTemps.Add(new V3ContainerMemberTemp
                        {
                            EntryPathIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            ContentHashIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            Length = checked((long)VarIntUtils.ReadVarInt<ulong>(r))
                        });
                    }

                    break;
                }

                case 0x09: // recipe payloads
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    recipePayloads = new List<byte[]>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        var len = checked((int)VarIntUtils.ReadVarInt<uint>(r));
                        recipePayloads.Add(r.ReadBytes(len));
                    }

                    break;
                }

                case 0x0A: // stats
                    ReadPackageStats(r, package);
                    break;

                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }

        package.CustomProperties = customProperties;
        package.OutputArtifacts = BuildOutputArtifacts(
            outputArtifactTemps,
            opaqueBackings,
            reconstructedBackings,
            memberTemps,
            recipePayloads,
            contentHashes,
            stringTable);

        return Task.FromResult(package);
    }

    private static Task<ReleasePackage> DeserializeV4Async(BinaryReader reader, Stream stream, CancellationToken cancellationToken)
    {
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;

        var contentHashes = new List<Hash32>();
        var tokenTable = new List<string>();
        var customProperties = new Dictionary<string, string>(StringComparer.Ordinal);

        var outputArtifactTemps = new List<V4OutputArtifactTemp>();
        var opaqueBackings = new List<V3OpaqueBackingTemp>();
        var reconstructedBackings = new List<V3ReconstructedBackingTemp>();
        var memberTemps = new List<V4ContainerMemberTemp>();
        var recipePayloads = new List<byte[]>();

        var package = new ReleasePackage();

        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            _ = reader.ReadByte(); // sectionFlags: Reserved for future usage
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var s = GetSectionStream(sectionId, stream, sectionSize, isCompressed);
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // metadata
                    ReadPackageMetadata(r, package);
                    break;

                case 0x02: // content hashes
                    contentHashes = ChecksumCompressor.TransposeDecompressHashes(s).ToList();
                    break;

                case 0x03: // token table (V4: path segments)
                    tokenTable = ReadStringTable(r);
                    package.StringTable = tokenTable;
                    break;

                case 0x04: // custom properties
                {
                    var propCount = VarIntUtils.ReadVarInt<uint>(r);
                    customProperties = new Dictionary<string, string>(checked((int)propCount), StringComparer.Ordinal);
                    for (var i = 0; i < propCount; i++)
                    {
                        var keyIndex = VarIntUtils.ReadVarInt<uint>(r);
                        var valueIndex = VarIntUtils.ReadVarInt<uint>(r);
                        customProperties[tokenTable[checked((int)keyIndex)]] = tokenTable[checked((int)valueIndex)];
                    }
                    break;
                }

                case 0x05: // output artifacts (V4: path as token sequence, no ComponentNameIndex, no BackingIndex)
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    outputArtifactTemps = new List<V4OutputArtifactTemp>(checked((int)count));

                    int opaqueCount = 0, reconstructedCount = 0;
                    for (var i = 0; i < count; i++)
                    {
                        var tokenCount = checked((int)VarIntUtils.ReadVarInt<uint>(r));
                        var pathTokenIndices = new int[tokenCount];
                        for (var k = 0; k < tokenCount; k++)
                            pathTokenIndices[k] = checked((int)VarIntUtils.ReadVarInt<uint>(r));

                        var kind = (OutputArtifactKind)r.ReadByte();
                        var bytePerfect = r.ReadByte() != 0;
                        var backingType = (BackingType)r.ReadByte();

                        // BackingIndex is implicit: ordinal position among artifacts of the same BackingType
                        var backingIndex = backingType == BackingType.OpaqueBlob
                            ? opaqueCount++
                            : reconstructedCount++;

                        outputArtifactTemps.Add(new V4OutputArtifactTemp
                        {
                            PathTokenIndices = pathTokenIndices,
                            Kind = kind,
                            RequiresBytePerfectReconstruction = bytePerfect,
                            BackingType = backingType,
                            BackingIndex = backingIndex
                        });
                    }

                    break;
                }

                case 0x06: // opaque backings
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    opaqueBackings = new List<V3OpaqueBackingTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        opaqueBackings.Add(new V3OpaqueBackingTemp
                        {
                            ContentHashIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            Length = checked((long)VarIntUtils.ReadVarInt<ulong>(r))
                        });
                    }

                    break;
                }

                case 0x07: // reconstructed backings
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    reconstructedBackings = new List<V3ReconstructedBackingTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        reconstructedBackings.Add(new V3ReconstructedBackingTemp
                        {
                            FormatIdIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            ReconstructionKind = (ReconstructionKind)r.ReadByte(),
                            MemberStart = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            MemberCount = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            RecipePayloadIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r))
                        });
                    }

                    break;
                }

                case 0x08: // container members (V4: entry path as token sequence)
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    memberTemps = new List<V4ContainerMemberTemp>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        var tokenCount = checked((int)VarIntUtils.ReadVarInt<uint>(r));
                        var entryPathTokenIndices = new int[tokenCount];
                        for (var k = 0; k < tokenCount; k++)
                            entryPathTokenIndices[k] = checked((int)VarIntUtils.ReadVarInt<uint>(r));

                        memberTemps.Add(new V4ContainerMemberTemp
                        {
                            EntryPathTokenIndices = entryPathTokenIndices,
                            ContentHashIndex = checked((int)VarIntUtils.ReadVarInt<uint>(r)),
                            Length = checked((long)VarIntUtils.ReadVarInt<ulong>(r))
                        });
                    }

                    break;
                }

                case 0x09: // recipe payloads
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    recipePayloads = new List<byte[]>(checked((int)count));

                    for (var i = 0; i < count; i++)
                    {
                        var len = checked((int)VarIntUtils.ReadVarInt<uint>(r));
                        recipePayloads.Add(r.ReadBytes(len));
                    }

                    break;
                }

                case 0x0A: // stats
                    ReadPackageStats(r, package);
                    break;

                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }

        package.CustomProperties = customProperties;
        package.OutputArtifacts = BuildOutputArtifacts(
            outputArtifactTemps,
            opaqueBackings,
            reconstructedBackings,
            memberTemps,
            recipePayloads,
            contentHashes,
            tokenTable);

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
    
    private static string CombineComponentAndFilePath(string componentName, string fileName)
    {
        var normalizedComponent = componentName.Replace('\\', '/').Trim('/');
        var normalizedFile = fileName.Replace('\\', '/').Trim('/');

        if (string.IsNullOrWhiteSpace(normalizedComponent) ||
            normalizedComponent.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFile;
        }

        if (string.IsNullOrWhiteSpace(normalizedFile))
            return normalizedComponent;

        return $"{normalizedComponent}/{normalizedFile}";
    }
    
    private static List<Hash32> CollectContentHashes(ReleasePackage package)
    {
        var hashes = new HashSet<Hash32>();

        foreach (var artifact in package.OutputArtifacts)
        {
            switch (artifact.Backing)
            {
                case OpaqueBlobBacking opaque:
                    if (opaque.ContentHash == null)
                        throw new InvalidDataException($"Output artifact '{artifact.Path}' is missing opaque content hash.");
                    hashes.Add(opaque.ContentHash.Value);
                    break;

                case ReconstructedContainerBacking reconstructed:
                    foreach (var member in reconstructed.Members)
                    {
                        if (member.ContentHash == null)
                            throw new InvalidDataException($"Output artifact '{artifact.Path}' has member '{member.EntryPath}' without a content hash.");
                        hashes.Add(member.ContentHash.Value);
                    }

                    break;

                default:
                    throw new NotSupportedException($"Unsupported artifact backing type: {artifact.Backing.GetType().FullName}");
            }
        }

        return hashes.OrderBy(x => x).ToList();
    }
    
    private static void WriteStringTable(BinaryWriter w, List<string> stringTable)
    {
        VarIntUtils.WriteVarInt(w, (uint)stringTable.Count);

        var bytes = new List<byte[]>(stringTable.Count);
        foreach (var s in stringTable)
        {
            var b = Encoding.UTF8.GetBytes(s);
            bytes.Add(b);
            VarIntUtils.WriteVarInt(w, (uint)b.Length);
        }

        foreach (var b in bytes)
            w.Write(b);
    }

    private static List<string> ReadStringTable(BinaryReader r)
    {
        var entryCount = checked((int)VarIntUtils.ReadVarInt<uint>(r));
        var lengths = new int[entryCount];
        var maxLen = 0;

        for (var i = 0; i < entryCount; i++)
        {
            var len = checked((int)VarIntUtils.ReadVarInt<uint>(r));
            lengths[i] = len;
            if (len > maxLen) maxLen = len;
        }

        var result = new List<string>(entryCount);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, maxLen));
        try
        {
            for (var i = 0; i < entryCount; i++)
            {
                var len = lengths[i];
                r.BaseStream.ReadExactly(buffer, 0, len);
                result.Add(Encoding.UTF8.GetString(buffer, 0, len));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    private static List<OutputArtifact> BuildOutputArtifacts(List<V3OutputArtifactTemp> outputArtifactTemps, List<V3OpaqueBackingTemp> opaqueBackings, List<V3ReconstructedBackingTemp> reconstructedBackings, List<V3ContainerMemberTemp> memberTemps, List<byte[]> recipePayloads, List<Hash32> contentHashes, List<string> stringTable)
    {
        var result = new List<OutputArtifact>(outputArtifactTemps.Count);

        foreach (var artifactTemp in outputArtifactTemps)
        {
            ArtifactBacking backing = artifactTemp.BackingType switch
            {
                BackingType.OpaqueBlob => BuildOpaqueBacking(opaqueBackings[artifactTemp.BackingIndex], contentHashes),
                BackingType.ReconstructedContainer => BuildReconstructedBacking(reconstructedBackings[artifactTemp.BackingIndex], memberTemps, recipePayloads, contentHashes, stringTable),
                _ => throw new InvalidDataException($"Unknown backing type: {artifactTemp.BackingType}")
            };

            result.Add(new OutputArtifact
            {
                Path = stringTable[artifactTemp.PathIndex],
                ComponentName = stringTable[artifactTemp.ComponentNameIndex],
                Kind = artifactTemp.Kind,
                RequiresBytePerfectReconstruction = artifactTemp.RequiresBytePerfectReconstruction,
                Backing = backing
            });
        }

        return result;
    }

    private static OpaqueBlobBacking BuildOpaqueBacking(V3OpaqueBackingTemp temp, List<Hash32> contentHashes)
    {
        return new OpaqueBlobBacking
        {
            ContentHash = contentHashes[temp.ContentHashIndex],
            Length = temp.Length
        };
    }

    private static ReconstructedContainerBacking BuildReconstructedBacking(V3ReconstructedBackingTemp temp, List<V3ContainerMemberTemp> memberTemps, List<byte[]> recipePayloads, List<Hash32> contentHashes, List<string> stringTable)
    {
        var members = new List<ContainerMemberBinding>(temp.MemberCount);
        for (var i = 0; i < temp.MemberCount; i++)
        {
            var m = memberTemps[temp.MemberStart + i];
            members.Add(new ContainerMemberBinding
            {
                EntryPath = stringTable[m.EntryPathIndex],
                ContentHash = contentHashes[m.ContentHashIndex],
                Length = m.Length
            });
        }

        return new ReconstructedContainerBacking
        {
            FormatId = stringTable[temp.FormatIdIndex],
            ReconstructionKind = temp.ReconstructionKind,
            Members = members,
            RecipePayload = recipePayloads[temp.RecipePayloadIndex]
        };
    }

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

    // V4 helpers -----------------------------------------------------------

    /// <summary>
    /// Splits a forward-slash-delimited path into its segment tokens.
    /// Empty segments from leading/trailing slashes are preserved as empty strings.
    /// </summary>
    private static string[] SplitPathToTokens(string path)
    {
        return path.Split('/');
    }

    /// <summary>
    /// Joins path segment tokens back into a path string.
    /// </summary>
    private static string JoinTokensToPath(int[] tokenIndices, List<string> tokenTable)
    {
        return string.Join('/', tokenIndices.Select(i => tokenTable[i]));
    }

    /// <summary>
    /// Builds the V4 token table: all distinct path segments across artifacts, member
    /// entry paths, format IDs, and custom property keys/values.  Returns the sorted
    /// table (as a list) and an ordinal lookup dictionary.
    /// </summary>
    private static (List<string> table, Dictionary<string, int> index) BuildTokenTable(ReleasePackage package)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var artifact in package.OutputArtifacts)
        {
            foreach (var seg in SplitPathToTokens(artifact.Path))
                values.Add(seg);

            if (artifact.Backing is ReconstructedContainerBacking reconstructed)
            {
                values.Add(reconstructed.FormatId ?? "");
                foreach (var member in reconstructed.Members)
                {
                    foreach (var seg in SplitPathToTokens(member.EntryPath))
                        values.Add(seg);
                }
            }
        }

        foreach (var kvp in package.CustomProperties)
        {
            // Custom property keys/values are treated as single tokens (not split)
            values.Add(kvp.Key);
            values.Add(kvp.Value);
        }

        var table = values
            .Select(x => (Text: x, Bytes: Encoding.UTF8.GetBytes(x)))
            .OrderBy(x => x.Bytes, ByteArrayComparer.Instance)
            .Select(x => x.Text)
            .ToList();

        var index = table
            .Select((s, i) => (s, i))
            .ToDictionary(x => x.s, x => x.i, StringComparer.Ordinal);

        return (table, index);
    }

    private static List<OutputArtifact> BuildOutputArtifacts(
        List<V4OutputArtifactTemp> outputArtifactTemps,
        List<V3OpaqueBackingTemp> opaqueBackings,
        List<V3ReconstructedBackingTemp> reconstructedBackings,
        List<V4ContainerMemberTemp> memberTemps,
        List<byte[]> recipePayloads,
        List<Hash32> contentHashes,
        List<string> tokenTable)
    {
        var result = new List<OutputArtifact>(outputArtifactTemps.Count);

        foreach (var artifactTemp in outputArtifactTemps)
        {
            var path = JoinTokensToPath(artifactTemp.PathTokenIndices, tokenTable);
            // ComponentName is the first path segment
            var componentName = artifactTemp.PathTokenIndices.Length > 0
                ? tokenTable[artifactTemp.PathTokenIndices[0]]
                : "";

            ArtifactBacking backing = artifactTemp.BackingType switch
            {
                BackingType.OpaqueBlob => BuildOpaqueBacking(opaqueBackings[artifactTemp.BackingIndex], contentHashes),
                BackingType.ReconstructedContainer => BuildReconstructedBacking(
                    reconstructedBackings[artifactTemp.BackingIndex],
                    memberTemps,
                    recipePayloads,
                    contentHashes,
                    tokenTable),
                _ => throw new InvalidDataException($"Unknown backing type: {artifactTemp.BackingType}")
            };

            result.Add(new OutputArtifact
            {
                Path = path,
                ComponentName = componentName,
                Kind = artifactTemp.Kind,
                RequiresBytePerfectReconstruction = artifactTemp.RequiresBytePerfectReconstruction,
                Backing = backing
            });
        }

        return result;
    }

    private static ReconstructedContainerBacking BuildReconstructedBacking(
        V3ReconstructedBackingTemp temp,
        List<V4ContainerMemberTemp> memberTemps,
        List<byte[]> recipePayloads,
        List<Hash32> contentHashes,
        List<string> tokenTable)
    {
        var members = new List<ContainerMemberBinding>(temp.MemberCount);
        for (var i = 0; i < temp.MemberCount; i++)
        {
            var m = memberTemps[temp.MemberStart + i];
            members.Add(new ContainerMemberBinding
            {
                EntryPath = JoinTokensToPath(m.EntryPathTokenIndices, tokenTable),
                ContentHash = contentHashes[m.ContentHashIndex],
                Length = m.Length
            });
        }

        return new ReconstructedContainerBacking
        {
            FormatId = tokenTable[temp.FormatIdIndex],
            ReconstructionKind = temp.ReconstructionKind,
            Members = members,
            RecipePayload = recipePayloads[temp.RecipePayloadIndex]
        };
    }

}

internal sealed class OutputArtifactRecord
{
    public required int[] PathTokenIndices { get; init; }
    public OutputArtifactKind Kind { get; init; }
    public bool RequiresBytePerfectReconstruction { get; init; }
    public BackingType BackingType { get; init; }
    public int BackingIndex { get; init; }
}

internal enum BackingType : byte
{
    OpaqueBlob = 0,
    ReconstructedContainer = 1
}

internal sealed class ReconstructedBackingRecord
{
    public int FormatIdIndex { get; init; }
    public ReconstructionKind ReconstructionKind { get; init; }
    public int MemberStart { get; init; }
    public int MemberCount { get; init; }
    public int RecipePayloadIndex { get; init; }
}

internal sealed class V3OutputArtifactTemp
{
    public int PathIndex { get; init; }
    public int ComponentNameIndex { get; init; }
    public OutputArtifactKind Kind { get; init; }
    public bool RequiresBytePerfectReconstruction { get; init; }
    public BackingType BackingType { get; init; }
    public int BackingIndex { get; init; }
}

internal sealed class V3OpaqueBackingTemp
{
    public int ContentHashIndex { get; init; }
    public long Length { get; init; }
}

internal sealed class V3ReconstructedBackingTemp
{
    public int FormatIdIndex { get; init; }
    public ReconstructionKind ReconstructionKind { get; init; }
    public int MemberStart { get; init; }
    public int MemberCount { get; init; }
    public int RecipePayloadIndex { get; init; }
}

internal sealed class V3ContainerMemberTemp
{
    public int EntryPathIndex { get; init; }
    public int ContentHashIndex { get; init; }
    public long Length { get; init; }
}

// V4-specific types -------------------------------------------------------

internal sealed class V4ContainerMemberRecord
{
    public required int[] EntryPathTokenIndices { get; init; }
    public int ContentHashIndex { get; init; }
    public long Length { get; init; }
}

internal sealed class V4OutputArtifactTemp
{
    public required int[] PathTokenIndices { get; init; }
    public OutputArtifactKind Kind { get; init; }
    public bool RequiresBytePerfectReconstruction { get; init; }
    public BackingType BackingType { get; init; }
    public int BackingIndex { get; init; }
}

internal sealed class V4ContainerMemberTemp
{
    public required int[] EntryPathTokenIndices { get; init; }
    public int ContentHashIndex { get; init; }
    public long Length { get; init; }
}
