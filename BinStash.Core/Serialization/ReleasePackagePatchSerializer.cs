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
using System.Text;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Compression;
using BinStash.Core.IO;
using BinStash.Core.Serialization.Utils;
using ZstdNet;

namespace BinStash.Core.Serialization;

public abstract class ReleasePackagePatchSerializer : ReleasePackageSerializerBase
{
    private const string Magic = "BPKD";
    private const byte Version = 1;

    // Global flags
    private const byte CompressionFlag = 0b0000_0001;

    // --- Public API ----------------------------------------------------------

    public static async Task<byte[]> SerializeAsync(ReleasePackagePatch patch, ReleasePackageSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await SerializeAsync(stream, patch, options, cancellationToken);
        return stream.ToArray();
    }

    public static async Task SerializeAsync(Stream stream, ReleasePackagePatch patch, ReleasePackageSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ReleasePackageSerializerOptions.Default;
        var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Header
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);

        byte flags = 0;
        if (options.EnableCompression) flags |= CompressionFlag;
        writer.Write(flags);

        // ---------- PREP: patch-local substring table ----------
        var substr = new SubstringTableBuilder();
        SeedPatchStringTable(patch, substr);
        var patchStringTable = substr.Table;

        // ---------- Sections ----------

        // 0x01: Patch metadata
        await WriteSectionAsync(stream, 0x01, w =>
        {
            w.Write(patch.Version);
            w.Write(patch.ReleaseId);
            w.Write(patch.RepoId);
            w.Write(patch.ParentId ?? string.Empty);
            w.Write(patch.Notes ?? string.Empty);
            VarIntUtils.WriteVarInt(w, patch.Level);
            VarIntUtils.WriteVarInt(w, patch.CreatedAt.ToUnixTimeSeconds());
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x02: File hash dictionary patch (reuses Chunk* fields; values are 32-byte hashes)
        // Layout:
        //  - finalCount:  VarInt
        //  - insertCount: VarInt
        //  - inserts:     transpose-compressed blob of 32-byte entries (absent if insertCount==0)
        //  - runCount:    VarInt
        //  - runs:        [op(byte), len(VarInt)] * runCount   (0=keep, 1=delete, 2=insert)
        await WriteSectionAsync(stream, 0x02, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)patch.FileHashFinalCount);
            VarIntUtils.WriteVarInt(w, (uint)patch.FileHashInsertDict.Count);
            if (patch.FileHashInsertDict.Count > 0)
                w.Write(ChecksumCompressor.TransposeCompress(patch.FileHashInsertDict));

            VarIntUtils.WriteVarInt(w, (uint)patch.FileHashRuns.Count);
            foreach (var (op, len) in patch.FileHashRuns)
            {
                w.Write(op);
                VarIntUtils.WriteVarInt(w, len);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // 0x03: String table delta (tokenized by the patch-local table)
        await WriteSectionAsync(stream, 0x03, w =>
        {
            // 3a) write patch-local substring table
            VarIntUtils.WriteVarInt(w, (uint)patchStringTable.Count);
            foreach (var s in patchStringTable)
            {
                var byteCount = Encoding.UTF8.GetByteCount(s);
                VarIntUtils.WriteVarInt(w, (uint)byteCount);

                var rented = ArrayPool<byte>.Shared.Rent(byteCount);
                try
                {
                    var written = Encoding.UTF8.GetBytes(s, 0, s.Length, rented, 0);
                    w.Write(rented, 0, written);
                }
                finally { ArrayPool<byte>.Shared.Return(rented); }
            }

            // 3b) delta (Add/Modify carry tokenized values)
            VarIntUtils.WriteVarInt(w, (uint)patch.StringTableDelta.Count);
            foreach (var e in patch.StringTableDelta)
            {
                w.Write((byte)e.Op);
                VarIntUtils.WriteVarInt(w, e.Id);
                if (e.Op != PatchOperation.Remove)
                {
                    var tokens = substr.Tokenize(e.Value ?? string.Empty);
                    WriteTokenSequence(w, tokens);
                }
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        // Section: 0x04 - Custom Properties Delta
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)patch.CustomPropertiesDelta.Count);
            foreach (var e in patch.CustomPropertiesDelta)
            {
                w.Write((byte)e.Op);

                // Key (tokenized against patch-local table)
                var keyTokens = substr.Tokenize(e.Key);
                WriteTokenSequence(w, keyTokens);

                // Value only for Add/Modify
                if (e.Op != PatchOperation.Remove)
                {
                    var valTokens = substr.Tokenize(e.Value ?? string.Empty);
                    WriteTokenSequence(w, valTokens);
                }
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);

        
        // 0x05: Components/files
        await WriteSectionAsync(stream, 0x05, w =>
        {
            // Component inserts (full payloads)
            VarIntUtils.WriteVarInt(w, (uint)patch.ComponentInsert.Count);
            foreach (var comp in patch.ComponentInsert)
            {
                WriteTokenSequence(w, substr.Tokenize(comp.Name));

                VarIntUtils.WriteVarInt(w, (uint)comp.Files.Count);
                foreach (var f in comp.Files)
                {
                    WriteTokenSequence(w, substr.Tokenize(f.Name));

                    // Write the 32-byte hash (Hash32)
                    var hb = f.Hash.GetBytes();
                    if (hb.Length != 32) throw new InvalidDataException("Hash must be 32 bytes.");
                    w.Write(hb);
                }
            }

            // Component runs (KEEP/DEL/INS)
            VarIntUtils.WriteVarInt(w, (uint)patch.ComponentRuns.Count);
            foreach (var (op, len) in patch.ComponentRuns)
            {
                w.Write((byte)op);
                VarIntUtils.WriteVarInt(w, len);
            }

            // File scripts per existing component
            VarIntUtils.WriteVarInt(w, (uint)patch.FileEdits.Count);
            foreach (var (compName, edit) in patch.FileEdits)
            {
                WriteTokenSequence(w, substr.Tokenize(compName));

                // Inserts
                VarIntUtils.WriteVarInt(w, (uint)edit.Insert.Count);
                foreach (var f in edit.Insert)
                {
                    WriteTokenSequence(w, substr.Tokenize(f.Name));
                    var hb = f.Hash.GetBytes();
                    if (hb.Length != 32) throw new InvalidDataException("Hash32 must be 32 bytes.");
                    w.Write(hb);
                }

                // Runs
                VarIntUtils.WriteVarInt(w, (uint)edit.Runs.Count);
                foreach (var (op, len) in edit.Runs)
                {
                    w.Write((byte)op);
                    VarIntUtils.WriteVarInt(w, len);
                }

                // Modifies (only hash optional now)
                VarIntUtils.WriteVarInt(w, (uint)edit.Modifies.Count);
                foreach (var (name, hash /* byte[]? OR Hash32? */, _) in edit.Modifies)
                {
                    WriteTokenSequence(w, substr.Tokenize(name));
                    byte mFlags = 0;
                    if (hash != null) 
                        mFlags |= 0b0000_0001; // has hash
                    w.Write(mFlags);

                    if ((mFlags & 0x01) != 0)
                    {
                        var raw32 = hash!.Value.GetBytes();
                        if (raw32.Length != 32) throw new InvalidDataException("Modify Hash32 must be 32 bytes.");
                        w.Write(raw32);
                    }
                }
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
    }

    public static async Task<ReleasePackagePatch> DeserializeAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(data);
        return await DeserializeAsync(stream, cancellationToken);
    }

    public static Task<ReleasePackagePatch> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        // Header
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != Magic) throw new InvalidDataException("Invalid magic header");
        var version = reader.ReadByte();
        if (version != Version) throw new InvalidDataException("Unsupported version");
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;

        var patch = new ReleasePackagePatch();

        // Patch-local string table
        var patchStringTable = new List<string>();

        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var sectionFlags = reader.ReadByte(); // reserved
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var slice = new BoundedStream(stream, (int)sectionSize);
            var s = (Stream)(isCompressed ? new DecompressionStream(slice) : slice);
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // metadata
                    patch.Version = r.ReadString();
                    patch.ReleaseId = r.ReadString();
                    patch.RepoId = r.ReadString();
                    patch.ParentId = r.ReadString();
                    patch.Notes = r.ReadString();
                    patch.Level = VarIntUtils.ReadVarInt<int>(r);
                    patch.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(VarIntUtils.ReadVarInt<long>(r));
                    break;

                case 0x02: // file-hash dictionary patch (reuses Chunk* fields)
                {
                    patch.FileHashFinalCount = (int)VarIntUtils.ReadVarInt<uint>(r);

                    var insCount = VarIntUtils.ReadVarInt<uint>(r);
                    using var nds = new NonDisposingStream(s);
                    patch.FileHashInsertDict = insCount == 0
                        ? new List<byte[]>(0)
                        : ChecksumCompressor.TransposeDecompress(nds); // each entry must be 32 bytes

                    var runCount = VarIntUtils.ReadVarInt<uint>(r);
                    patch.FileHashRuns = new List<(byte Op, uint Len)>(checked((int)runCount));
                    for (var i = 0; i < runCount; i++)
                    {
                        var op = r.ReadByte();
                        var len = VarIntUtils.ReadVarInt<uint>(r);
                        patch.FileHashRuns.Add((op, len));
                    }

                    break;
                }

                case 0x03: // patch-local string table + delta
                {
                    var tblCount = VarIntUtils.ReadVarInt<uint>(r);
                    patchStringTable = new List<string>(checked((int)tblCount));
                    for (var i = 0; i < tblCount; i++)
                    {
                        var byteLen = VarIntUtils.ReadVarInt<uint>(r);
                        var bytes = r.ReadBytes(checked((int)byteLen));
                        patchStringTable.Add(Encoding.UTF8.GetString(bytes));
                    }

                    var deltaCount = VarIntUtils.ReadVarInt<uint>(r);
                    for (var i = 0; i < deltaCount; i++)
                    {
                        var op = (PatchOperation)r.ReadByte();
                        var id = VarIntUtils.ReadVarInt<ushort>(r);
                        string? value = null;
                        if (op != PatchOperation.Remove)
                            value = ReadTokenizedStringWithTable(r, patchStringTable);
                        patch.StringTableDelta.Add(new PatchStringEntry(op, id, value));
                    }

                    break;
                }

                case 0x04: // Custom Properties Delta
                {
                    var count = VarIntUtils.ReadVarInt<uint>(r);
                    patch.CustomPropertiesDelta = new List<PatchPropertyEntry>(checked((int)count));
                    for (var i = 0; i < count; i++)
                    {
                        var op = (PatchOperation)r.ReadByte();
                        var key = ReadTokenizedStringWithTable(r, patchStringTable);
                        string? value = null;
                        if (op != PatchOperation.Remove)
                            value = ReadTokenizedStringWithTable(r, patchStringTable);

                        patch.CustomPropertiesDelta.Add(new PatchPropertyEntry(op, key, value));
                    }
                    break;
                }
                
                case 0x05: // components/files
                { 
                    Span<byte> hashBuffer = stackalloc byte[32];
                    // Inserts
                    var compInsCount = VarIntUtils.ReadVarInt<uint>(r);
                    patch.ComponentInsert = new List<ComponentInsertPayload>(checked((int)compInsCount));
                    for (var i = 0; i < compInsCount; i++)
                    {
                        var compName = ReadTokenizedStringWithTable(r, patchStringTable);
                        var ins = new ComponentInsertPayload { Name = compName };

                        var fileInsCount = VarIntUtils.ReadVarInt<uint>(r);
                        ins.Files = new List<FileInsertPayload>(checked((int)fileInsCount));

                        for (var j = 0; j < fileInsCount; j++)
                        {
                            var fname = ReadTokenizedStringWithTable(r, patchStringTable);

                            r.BaseStream.ReadExactly(hashBuffer);
                            var h32 = new Hash32(hashBuffer.ToArray());

                            ins.Files.Add(new FileInsertPayload { Name = fname, Hash = h32 });
                        }

                        patch.ComponentInsert.Add(ins);
                    }

                    // Component runs
                    var compRunCount = VarIntUtils.ReadVarInt<uint>(r);
                    patch.ComponentRuns = new List<(ListOp Op, uint Len)>(checked((int)compRunCount));
                    for (var i = 0; i < compRunCount; i++)
                    {
                        var op = (ListOp)r.ReadByte();
                        var len = VarIntUtils.ReadVarInt<uint>(r);
                        patch.ComponentRuns.Add((op, len));
                    }

                    // File edits per component
                    var fileScriptCount = VarIntUtils.ReadVarInt<uint>(r);
                    patch.FileEdits = new Dictionary<string, FileListEdit>(checked((int)fileScriptCount), StringComparer.Ordinal);

                    for (var i = 0; i < fileScriptCount; i++)
                    {
                        var compName = ReadTokenizedStringWithTable(r, patchStringTable);
                        var edit = new FileListEdit();

                        // Inserts
                        var fileInsCount = VarIntUtils.ReadVarInt<uint>(r);
                        edit.Insert = new List<FileInsertPayload>(checked((int)fileInsCount));
                        for (var j = 0; j < fileInsCount; j++)
                        {
                            var fname = ReadTokenizedStringWithTable(r, patchStringTable);

                            r.BaseStream.ReadExactly(hashBuffer);
                            var h32 = new Hash32(hashBuffer.ToArray());

                            edit.Insert.Add(new FileInsertPayload { Name = fname, Hash = h32 });
                        }

                        // Runs
                        var runCount = VarIntUtils.ReadVarInt<uint>(r);
                        edit.Runs = new List<(ListOp Op, uint Len)>(checked((int)runCount));
                        for (var j = 0; j < runCount; j++)
                        {
                            var op = (ListOp)r.ReadByte();
                            var len = VarIntUtils.ReadVarInt<uint>(r);
                            edit.Runs.Add((op, len));
                        }

                        // Modifies (only hash optional)
                        var modCount = VarIntUtils.ReadVarInt<uint>(r);
                        edit.Modifies = new List<(string Name, Hash32? Hash, List<DeltaChunkRef>? Chunks)>(checked((int)modCount));
                        for (var j = 0; j < modCount; j++)
                        {
                            var name = ReadTokenizedStringWithTable(r, patchStringTable);
                            var mflags = r.ReadByte();

                            Hash32? hash = null;
                            if ((mflags & 0x01) != 0)
                            {
                                r.BaseStream.ReadExactly(hashBuffer);
                                hash = new Hash32(hashBuffer.ToArray());
                            }

                            edit.Modifies.Add((name, hash, null)); // Chunks are obsolete in v2
                        }

                        patch.FileEdits[compName] = edit;
                    }
                    break;
                }

                default:
                    throw new InvalidDataException($"Unknown section ID: {sectionId:X2}");
            }
        }

        return Task.FromResult(patch);
    }

    // --- Helpers -------------------------------------------------------------

    private static void SeedPatchStringTable(ReleasePackagePatch patch, SubstringTableBuilder substr)
    {
        // A) StringTableDelta values (Add/Modify only)
        foreach (var e in patch.StringTableDelta)
            if (e.Op != PatchOperation.Remove)
                _ = substr.Tokenize(e.Value ?? string.Empty);

        // B) Component inserts (names + files)
        foreach (var comp in patch.ComponentInsert)
        {
            _ = substr.Tokenize(comp.Name);
            foreach (var f in comp.Files)
                _ = substr.Tokenize(f.Name);
        }

        // C) File scripts per component (component name, insert file names, modify file names)
        foreach (var (compName, edit) in patch.FileEdits)
        {
            _ = substr.Tokenize(compName);
            foreach (var f in edit.Insert)
                _ = substr.Tokenize(f.Name);
            foreach (var (name, _, _) in edit.Modifies)
                _ = substr.Tokenize(name);
        }
    }

    private static void WriteTokenSequence(BinaryWriter w, List<(int id, Separator sep)> tokens)
    {
        VarIntUtils.WriteVarInt(w, tokens.Count);
        foreach (var (id, sep) in tokens)
        {
            VarIntUtils.WriteVarInt(w, (uint)id);
            w.Write((byte)sep);
        }
    }

    private static string ReadTokenizedStringWithTable(BinaryReader r, List<string> table)
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
}
