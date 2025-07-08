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

using System.Text;
using System.Text.RegularExpressions;
using BinStash.Contracts.Release;
using ZstdNet;

namespace BinStash.Core.Serialization;

public static class ReleasePackageSerializer
{
    private const string Magic = "BPKG";
    private const byte Version = 1;
    
    
    public static async Task<byte[]> SerializeAsync(ReleasePackage package, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await SerializeAsync(stream, package, null, cancellationToken);
        return stream.ToArray();
    }
    public static async Task SerializeAsync(Stream stream, ReleasePackage package, ReleasePackageSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ReleasePackageSerializerOptions.Default;
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        
        // Write magic, version and flags
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);
        writer.Write((byte)0); // flags (unused)

        // Section: 0x01 - Package metadata
        await WriteSectionAsync(stream, 0x01, w =>
        {
            w.Write(package.Version);
            w.Write(package.ReleaseId);
            w.Write(package.RepoId);
            w.Write(package.Notes ?? "");
            WriteVarInt(w, package.CreatedAt.ToUnixTimeSeconds());
        }, options, cancellationToken);

        // Section: 0x02 - Chunk table
        await WriteSectionAsync(stream, 0x02, w =>
        {
            WriteVarInt(w, (uint)package.Chunks.Count);
            foreach (var chunk in package.Chunks)
                w.Write(chunk.Checksum);
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
            WriteVarInt(w, (uint)package.StringTable.Count);
            foreach (var s in package.StringTable)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                WriteVarInt(w, (ushort)bytes.Length);
                w.Write(bytes);
            }
        }, options, cancellationToken);

        // Section: 0x04 - Components and files
        await WriteSectionAsync(stream, 0x04, w =>
        {
            WriteVarInt(w, (uint)tokenizedComponents.Count);
            for (var i = 0; i < tokenizedComponents.Count; i++)
            {
                var (compTokens, fileTokenLists) = tokenizedComponents[i];
                WriteTokenSequence(w, compTokens);
                WriteVarInt(w, (uint)fileTokenLists.Count);
                for (var j = 0; j < fileTokenLists.Count; j++)
                {
                    WriteTokenSequence(w, fileTokenLists[j]);

                    var file = package.Components[i].Files[j];
                    w.Write(file.Hash);

                    WriteVarInt(w, (uint)file.Chunks.Count);
                    
                    // Calculate bit widths for delta index, offset and length
                    var bitsDelta = (byte)Math.Ceiling(Math.Log2(file.Chunks.Max(c => c.DeltaIndex) + 1));
                    var bitsOffset = (byte)Math.Ceiling(Math.Log2(file.Chunks.Max(c => c.Offset) + 1));
                    var bitsLength = (byte)Math.Ceiling(Math.Log2(file.Chunks.Max(c => c.Length) + 1));

                    // Write bit widths
                    w.Write(bitsDelta);
                    w.Write(bitsOffset);
                    w.Write(bitsLength);

                    // Write bit-packed chunk data
                    var bitWriter = new BitWriter();
                    foreach (var chunk in file.Chunks)
                    {
                        bitWriter.WriteBits(chunk.DeltaIndex, bitsDelta);
                        bitWriter.WriteBits(chunk.Offset, bitsOffset);
                        bitWriter.WriteBits(chunk.Length, bitsLength);
                    }

                    var packed = bitWriter.ToArray();
                    WriteVarInt(w, (uint)packed.Length);
                    w.Write(packed);
                }
            }
        }, options, cancellationToken);

        // Section: 0x05 - Package statistics
        await WriteSectionAsync(stream, 0x05, w =>
        {
            WriteVarInt(w , package.Stats.FileCount);
            WriteVarInt(w, package.Stats.ChunkCount);
            WriteVarInt(w, package.Stats.UncompressedSize);
            WriteVarInt(w, package.Stats.CompressedSize);
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
        var flags = reader.ReadByte(); // unused

        var package = new ReleasePackage();
        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var compressedSize = ReadVarInt<uint>(reader);
            var uncompressedSize = ReadVarInt<uint>(reader);

            using var compressed = new MemoryStream(reader.ReadBytes((int)compressedSize));
            await using var z = new DecompressionStream(compressed);
            using var s = new MemoryStream();
            await z.CopyToAsync(s, cancellationToken);
            s.Position = 0;
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Package metadata
                    package.Version = r.ReadString();
                    package.ReleaseId = r.ReadString();
                    package.RepoId = r.ReadString();
                    package.Notes = r.ReadString();
                    package.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(ReadVarInt<long>(r));
                    break;
                case 0x02: // Section: 0x02 - Chunk table
                    var count = ReadVarInt<uint>(r);
                    for (var i = 0; i < count; i++)
                        package.Chunks.Add(new ChunkInfo(r.ReadBytes(32)));
                    break;
                case 0x03: // Section: 0x03 - String table
                    var entryCount = ReadVarInt<uint>(r);
                    for (var i = 0; i < entryCount; i++)
                    {
                        var len = ReadVarInt<ushort>(r);
                        package.StringTable.Add(Encoding.UTF8.GetString(r.ReadBytes(len)));
                    }
                    break;
                case 0x04: // Section: 0x04 - Components and files
                    var compCount = ReadVarInt<uint>(r);
                    for (var i = 0; i < compCount; i++)
                    {
                        var compName = ReadTokenizedString(r, package.StringTable);
                        var comp = new Component { Name = compName };
                        var fileCount = ReadVarInt<uint>(r);
                        for (var j = 0; j < fileCount; j++)
                        {
                            var fileName = ReadTokenizedString(r, package.StringTable);
                            var file = new ReleaseFile
                            {
                                Name = fileName,
                                Hash = r.ReadBytes(8) // 8 bytes for XxHash3, maybe make this configurable?
                            };
                            var chunkCount = ReadVarInt<uint>(r);
                            
                            var bitsDelta = r.ReadByte();
                            var bitsOffset = r.ReadByte();
                            var bitsLength = r.ReadByte();

                            var packedLength = ReadVarInt<uint>(r);
                            var packedData = r.ReadBytes((int)packedLength);

                            var bitReader = new BitReader(packedData);
                            for (var k = 0; k < chunkCount; k++)
                            {
                                var delta = (uint)bitReader.ReadBits(bitsDelta);
                                var offset = bitReader.ReadBits(bitsOffset);
                                var length = bitReader.ReadBits(bitsLength);
                                file.Chunks.Add(new DeltaChunkRef(delta, offset, length));
                            }

                            comp.Files.Add(file);
                        }
                        package.Components.Add(comp);
                    }
                    break;
                case 0x05: // Section: 0x05 - Package statistics
                    package.Stats.FileCount = ReadVarInt<uint>(r);
                    package.Stats.ChunkCount = ReadVarInt<uint>(r);
                    package.Stats.UncompressedSize = ReadVarInt<ulong>(r);
                    package.Stats.CompressedSize = ReadVarInt<ulong>(r);
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
            WriteVarInt(writer, (ulong)compressed.Length);
            WriteVarInt(writer, (ulong)ms.Length);
            await compressed.CopyToAsync(baseStream, ct);
        }
        else
        {
            // Write uncompressed section data
            var writer = new BinaryWriter(baseStream, Encoding.UTF8, true);
            writer.Write(id);
            WriteVarInt(writer, (ulong)ms.Length);
            WriteVarInt(writer, (ulong)ms.Length);
            await ms.CopyToAsync(baseStream, ct);
        }
    }
    
    private static void WriteVarInt<T>(BinaryWriter w, T value) where T : struct
    {
        switch (value)
        {
            case int i:
                WriteSignedVarInt(w, i);
                break;

            case long l:
                WriteSignedVarInt(w, l);
                break;

            case uint ui:
                WriteUnsignedVarInt(w, ui);
                break;

            case ulong ul:
                WriteUnsignedVarInt(w, ul);
                break;

            case ushort us:
                WriteUnsignedVarInt(w, us);
                break;

            default:
                throw new NotSupportedException($"Type {typeof(T)} is not supported for varint serialization.");
        }
    }
    
    private static void WriteSignedVarInt(BinaryWriter w, long value)
    {
        var zigZag = (ulong)((value << 1) ^ (value >> 63)); // ZigZag encoding
        WriteUnsignedVarInt(w, zigZag);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, ulong value)
    {
        while ((value & ~0x7FUL) != 0)
        {
            w.Write((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        w.Write((byte)value);
    }

    private static void WriteUnsignedVarInt(BinaryWriter w, uint value)
    {
        while ((value & ~0x7FU) != 0)
        {
            w.Write((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        w.Write((byte)value);
    }
    
    private static T ReadVarInt<T>(BinaryReader r) where T : struct
    {
        var result = typeof(T) switch
        {
            { } t when t == typeof(int) => ReadSignedVarInt32(r),
            { } t when t == typeof(long) => ReadSignedVarInt64(r),
            { } t when t == typeof(uint) => ReadUnsignedVarInt32(r),
            { } t when t == typeof(ulong) => ReadUnsignedVarInt64(r),
            { } t when t == typeof(ushort) => (object)(ushort)ReadUnsignedVarInt32(r),
            _ => throw new NotSupportedException($"Type {typeof(T)} is not supported for varint deserialization.")
        };
        return (T)result;
    }
    
    private static uint ReadUnsignedVarInt32(BinaryReader r)
    {
        uint result = 0;
        var shift = 0;

        while (true)
        {
            var b = r.ReadByte();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35)
                throw new FormatException("VarInt32 too long.");
        }

        return result;
    }

    private static ulong ReadUnsignedVarInt64(BinaryReader r)
    {
        ulong result = 0;
        var shift = 0;

        while (true)
        {
            var b = r.ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 70)
                throw new FormatException("VarInt64 too long.");
        }

        return result;
    }

    private static int ReadSignedVarInt32(BinaryReader r)
    {
        var raw = ReadUnsignedVarInt32(r);
        return (int)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }

    private static long ReadSignedVarInt64(BinaryReader r)
    {
        var raw = ReadUnsignedVarInt64(r);
        return (long)((raw >> 1) ^ (~(raw & 1) + 1)); // ZigZag decode
    }
    
    private static void WriteTokenSequence(BinaryWriter w, List<(ushort id, Separator sep)> tokens)
    {
        WriteVarInt(w, (ushort)tokens.Count);
        foreach (var (id, sep) in tokens)
        {
            WriteVarInt(w, id);
            w.Write((byte)sep);
        }
    }
    
    private static string ReadTokenizedString(BinaryReader r, List<string> table)
    {
        var count = ReadVarInt<ushort>(r);
        var sb = new StringBuilder();

        for (var i = 0; i < count; i++)
        {
            var id = ReadVarInt<ushort>(r);
            var sep = (Separator)r.ReadByte();
            sb.Append(table[id]);
            if (sep != Separator.None)
                sb.Append((char)sep);
        }

        return sb.ToString();
    }
}

public class ReleasePackageSerializerOptions
{
    public static ReleasePackageSerializerOptions Default { get; } = new();
    
    public bool EnableCompression { get; set; } = true;
    public int CompressionLevel { get; set; } = 3;
}

internal enum Separator : byte
{
    None = 0,
    Dot = (byte)'.',
    Slash = (byte)'/',
    Backslash = (byte)'\\',
}

internal class SubstringTableBuilder
{
    private readonly Dictionary<string, ushort> _Index = new();
    public readonly List<string> Table = new();

    public List<(ushort id, Separator sep)> Tokenize(string input)
    {
        var tokens = new List<(ushort, Separator)>();

        var regex = new Regex(@"([\\/\.])", RegexOptions.Compiled);
        var matches = regex.Split(input); // includes separators as separate entries

        for (var i = 0; i < matches.Length; i += 2)
        {
            var part = matches[i];
            var sep = i + 1 < matches.Length ? ToSep(matches[i + 1]) : Separator.None;

            var id = GetOrAdd(part);
            tokens.Add((id, sep));
        }

        return tokens;
    }

    private ushort GetOrAdd(string str)
    {
        if (_Index.TryGetValue(str, out var id)) return id;
        id = (ushort)Table.Count;
        Table.Add(str);
        _Index[str] = id;
        return id;
    }

    private static Separator ToSep(string s) => s switch
    {
        "." => Separator.Dot,
        "/" => Separator.Slash,
        "\\" => Separator.Backslash,
        _ => Separator.None
    };
}

internal sealed class BitWriter
{
    private readonly List<byte> _Buffer = new();
    private byte _Current;
    private int _BitCount;

    public void WriteBits(ulong value, int bitCount)
    {
        for (var i = 0; i < bitCount; i++)
        {
            var bit = (value >> i) & 1;
            _Current |= (byte)(bit << _BitCount++);
            if (_BitCount != 8) continue;
            _Buffer.Add(_Current);
            _Current = 0;
            _BitCount = 0;
        }
    }

    public byte[] ToArray()
    {
        if (_BitCount > 0) _Buffer.Add(_Current);
        return _Buffer.ToArray();
    }
}

internal sealed class BitReader
{
    private readonly byte[] _Data;
    private int _ByteIndex;
    private int _BitIndex;

    public BitReader(byte[] data) => _Data = data;

    public ulong ReadBits(int bitCount)
    {
        ulong result = 0;
        for (var i = 0; i < bitCount; i++)
        {
            var bit = (_Data[_ByteIndex] >> _BitIndex++) & 1;
            result |= ((ulong)bit << i);

            if (_BitIndex != 8) continue;
            _BitIndex = 0;
            _ByteIndex++;
        }
        return result;
    }
}
