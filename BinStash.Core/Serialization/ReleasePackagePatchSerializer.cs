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
using System.Text;
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
    // Flags
    private const byte CompressionFlag = 0b0000_0001;
    
    // Small file-flags for components section
    private const byte FILE_HAS_HASH    = 1 << 0; // hash present
    private const byte FILE_CHUNK_KIND0 = 0 << 1; // inline chunk-refs
    private const byte FILE_CHUNK_KIND1 = 1 << 1; // ref by local content-id index (section 0x04 order)
    private const byte FILE_CHUNK_KIND2 = 2 << 1; // ref by literal 64-bit content-id
    private const byte FILE_CHUNK_MASK  = 3 << 1;
    private const byte NAMES_BY_ID = 0b0000_0001;


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
        
        // Write magic, version and flags
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(Version);

        byte flags = 0;
        if (options.EnableCompression)
            flags |= CompressionFlag;
        
        writer.Write(flags);
        
        // ---------- PREP: build helpers for compact encoding ----------
        var substr = new SubstringTableBuilder();
        
        SeedPatchStringTable(patch, substr);
        
        var patchStringTable = substr.Table; // patch-local table for tokenization
        
        var nameToId = new Dictionary<string, uint>(patchStringTable.Count, StringComparer.Ordinal);
        for (uint i = 0; i < patchStringTable.Count; i++)
            nameToId[patchStringTable[(int)i]] = i;

        // 3) Prepare ContentIdDelta: sort by CID and delta-encode ids (varint)
        //    Also build an index map so files can reference by local index (cheaper than literal 64-bit CID).
        var sortedCid = patch.ContentIdDelta.ToList();
        sortedCid.Sort((a, b) => a.ContentId.CompareTo(b.ContentId));
        var cidIndex = new Dictionary<ulong, uint>(sortedCid.Count);
        for (var i = 0; i < sortedCid.Count; i++)
            cidIndex[sortedCid[i].ContentId] = (uint)i;
        
        // Section: 0x01 - Patch Metadata
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
        
        // Section: 0x02 - Chunk table patches
        await WriteSectionAsync(stream, 0x02, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)patch.ChunkFinalCount);
            VarIntUtils.WriteVarInt(w, (uint)patch.ChunkInsertDict.Count);
            if (patch.ChunkInsertDict.Count > 0)
                w.Write(ChecksumCompressor.TransposeCompress(patch.ChunkInsertDict));
            
            VarIntUtils.WriteVarInt(w, (uint)patch.ChunkRuns.Count);
            foreach (var (op,len) in patch.ChunkRuns)
            {
                w.Write(op); // Op: 0=keep, 1=delete, 2=insert
                VarIntUtils.WriteVarInt(w, len);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Section: 0x03 - String Table
        await WriteSectionAsync(stream, 0x03, w =>
        {
            // 3a) write patch-local table
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

            // 3b) write string-delta using tokens (values already seeded; Tokenize won't add new table entries)
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
        
        // Section: 0x04 - Content IDs
        await WriteSectionAsync(stream, 0x04, w =>
        {
            VarIntUtils.WriteVarInt(w, (uint)sortedCid.Count);
            var prev = 0UL;
            foreach (var e in sortedCid)
            {
                w.Write((byte)e.Op);
                var delta = e.ContentId - prev; // unsigned, cids sorted ascending
                VarIntUtils.WriteVarInt(w, delta);
                prev = e.ContentId;

                if (e.Op != PatchOperation.Remove)
                    WriteChunkRefs(w, e.Chunks!);
            }
        }, options.EnableCompression, options.CompressionLevel, cancellationToken);
        
        // Section: 0x05 - Components
        await WriteSectionAsync(stream, 0x05, w =>
        {
            // These are full components that do not exist in the parent (in child order).
            VarIntUtils.WriteVarInt(w, (uint)patch.ComponentInsert.Count);
            foreach (var comp in patch.ComponentInsert)
            {
                WriteTokenSequence(w, substr.Tokenize(comp.Name));

                // Files inside this new component (full payloads)
                VarIntUtils.WriteVarInt(w, (uint)comp.Files.Count);
                
                // Compute flags per file
                var flagsArr = new byte[comp.Files.Count];
                for (var i = 0; i < comp.Files.Count; i++)
                {
                    var f = comp.Files[i];
                    byte fflags = 0;
                    if (f.Hash != 0) fflags |= FILE_HAS_HASH;

                    if (f.Chunks.Count == 0) fflags |= FILE_CHUNK_KIND0;
                    else
                    {
                        var cid = ComputeContentId(f.Chunks);
                        if (cidIndex.TryGetValue(cid, out var idx))
                            fflags |= FILE_CHUNK_KIND1;
                        else
                            fflags |= FILE_CHUNK_KIND0; // or KIND2 if you enable literal CIDs
                    }
                    flagsArr[i] = fflags;
                }
                
                // Pick most frequent flags as commonFlags
                var commonFlags = flagsArr
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? 0;

                w.Write(commonFlags);
                
                // bitmap of overrides
                var overrideMask = new bool[flagsArr.Length];
                for (var i = 0; i < flagsArr.Length; i++)
                    overrideMask[i] = flagsArr[i] != commonFlags;
                WriteOverrideBitmap(w, overrideMask);
                
                // For each file: OPTIONAL per-file flags (only if override), then payload according to effective flags
                for (var i = 0; i < comp.Files.Count; i++)
                {
                    var f = comp.Files[i];
                    var effFlags = flagsArr[i];

                    WriteTokenSequence(w, substr.Tokenize(f.Name));

                    if (overrideMask[i])
                        w.Write(effFlags); // only when different from commonFlags

                    if ((effFlags & FILE_HAS_HASH) != 0)
                    {
                        Span<byte> hb = stackalloc byte[8];
                        BinaryPrimitives.WriteUInt64LittleEndian(hb, f.Hash);
                        w.Write(hb);
                    }

                    switch (effFlags & FILE_CHUNK_MASK)
                    {
                        case FILE_CHUNK_KIND0:
                            WriteChunkRefs(w, f.Chunks);
                            break;
                        case FILE_CHUNK_KIND1:
                        {
                            var cid = ComputeContentId(f.Chunks);
                            VarIntUtils.WriteVarInt(w, cidIndex[cid]);
                            break;
                        }
                        case FILE_CHUNK_KIND2:
                            // If you enable literal CIDs: write 8 bytes LE here
                            throw new NotSupportedException();
                    }
                }
            }
            
            // The compact edit script that transforms parent.Components -> child.Components
            VarIntUtils.WriteVarInt(w, (uint)patch.ComponentRuns.Count);
            foreach (var (op, len) in patch.ComponentRuns)
            {
                w.Write((byte)op); // op: 0 = keep, 1 = delete, 2 = insert
                VarIntUtils.WriteVarInt(w, len);
            }
            
            // For components that exist in both parent & child we ship a file-list script.
            // Keyed by component name, each entry has: Insert payloads, Runs, and Modifies for kept files.
            VarIntUtils.WriteVarInt(w, (uint)patch.FileEdits.Count);
            foreach (var kv in patch.FileEdits)
            {
                var compName = kv.Key;
                var edit = kv.Value;

                // Component name
                WriteTokenSequence(w, substr.Tokenize(compName));

                // File INSERT payloads
                VarIntUtils.WriteVarInt(w, (uint)edit.Insert.Count);
                
                var flagsArr = new byte[edit.Insert.Count];
                for (var i = 0; i < edit.Insert.Count; i++)
                {
                    var f = edit.Insert[i];
                    byte fflags = 0;
                    if (f.Hash != 0) fflags |= FILE_HAS_HASH;

                    if (f.Chunks.Count == 0) fflags |= FILE_CHUNK_KIND0;
                    else
                    {
                        var cid = ComputeContentId(f.Chunks);
                        if (cidIndex.TryGetValue(cid, out var idx)) fflags |= FILE_CHUNK_KIND1;
                        else                                        fflags |= FILE_CHUNK_KIND0;
                    }
                    flagsArr[i] = fflags;
                }

                var common = flagsArr
                    .GroupBy(x => x)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? (byte)0;

                w.Write(common);

                var overrides = new bool[flagsArr.Length];
                for (var i = 0; i < flagsArr.Length; i++) overrides[i] = flagsArr[i] != common;
                WriteOverrideBitmap(w, overrides);
                
                for (var i = 0; i < edit.Insert.Count; i++)
                {
                    var f = edit.Insert[i];
                    var eff = flagsArr[i];

                    WriteTokenSequence(w, substr.Tokenize(f.Name));

                    if (overrides[i]) w.Write(eff);

                    if ((eff & FILE_HAS_HASH) != 0)
                    {
                        Span<byte> hb = stackalloc byte[8];
                        BinaryPrimitives.WriteUInt64LittleEndian(hb, f.Hash);
                        w.Write(hb);
                    }

                    switch (eff & FILE_CHUNK_MASK)
                    {
                        case FILE_CHUNK_KIND0:
                            WriteChunkRefs(w, f.Chunks);
                            break;
                        case FILE_CHUNK_KIND1:
                        {
                            var cid = ComputeContentId(f.Chunks);
                            VarIntUtils.WriteVarInt(w, cidIndex[cid]);
                            break;
                        }
                        default:
                            throw new InvalidDataException("Unknown file chunk encoding kind.");
                    }
                }

                // File RUNS (KEEP/DEL/INS)
                VarIntUtils.WriteVarInt(w, (uint)edit.Runs.Count);
                foreach (var (op, len) in edit.Runs)
                {
                    w.Write((byte)op);
                    VarIntUtils.WriteVarInt(w, len);
                }

                // File MODIFIES for kept files (optional hash/chunks)
                VarIntUtils.WriteVarInt(w, (uint)edit.Modifies.Count);
                foreach (var (name, hash, chunks) in edit.Modifies)
                {
                    WriteTokenSequence(w, substr.Tokenize(name));
                    byte mflags = 0;
                    if (hash != null) mflags |= 0b0000_0001; // has hash
                    if (chunks != null) mflags |= 0b0000_0010; // has chunks
                    w.Write(mflags);

                    if ((mflags & 0x01) != 0) w.Write(hash!);
                    if ((mflags & 0x02) != 0) WriteChunkRefs(w, chunks!);
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

        // Read magic, version and flags
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != Magic) throw new InvalidDataException("Invalid magic header");
        var version = reader.ReadByte();
        if (version != Version) throw new InvalidDataException("Unsupported version");
        var flags = reader.ReadByte();
        var isCompressed = (flags & CompressionFlag) != 0;

        var patch = new ReleasePackagePatch();

        // Patch-local tables built along the way
        var patchStringTable = new List<string>(); // from 0x03
        var contentIdEntries = new List<List<DeltaChunkRef>?>(); // from 0x04 (ordered, may contain null for Remove)
        var contentIdLookup = new Dictionary<ulong, List<DeltaChunkRef>>(); // for literal CID refs

        while (stream.Position < stream.Length)
        {
            var sectionId = reader.ReadByte();
            var sectionFlags = reader.ReadByte(); // Currently unused, reserved for future use
            var sectionSize = VarIntUtils.ReadVarInt<uint>(reader);

            using var slice = new BoundedStream(stream, (int)sectionSize);
            var s = (Stream)(isCompressed ? new DecompressionStream(slice) : slice);
            using var r = new BinaryReader(s);

            switch (sectionId)
            {
                case 0x01: // Section: 0x01 - Patch Metadata
                    patch.Version = r.ReadString();
                    patch.ReleaseId = r.ReadString();
                    patch.RepoId = r.ReadString();
                    patch.ParentId = r.ReadString();
                    patch.Notes = r.ReadString();
                    patch.Level = VarIntUtils.ReadVarInt<int>(s);
                    patch.CreatedAt = DateTimeOffset.FromUnixTimeSeconds(VarIntUtils.ReadVarInt<long>(s));
                    break;
                case 0x02: // Section: 0x02 - Chunks
                {
                    patch.ChunkFinalCount = (int)VarIntUtils.ReadVarInt<uint>(s);

                    var insCount = VarIntUtils.ReadVarInt<uint>(s);

                    using var nds = new NonDisposingStream(s);
                    patch.ChunkInsertDict = insCount == 0 ? new List<byte[]>(0) : ChecksumCompressor.TransposeDecompress(nds);

                    var runCount = VarIntUtils.ReadVarInt<uint>(s);
                    patch.ChunkRuns = new List<(byte Op, uint Len)>(checked((int)runCount));
                    for (var i = 0; i < runCount; i++)
                    {
                        var op = r.ReadByte();
                        var len = VarIntUtils.ReadVarInt<uint>(s);
                        patch.ChunkRuns.Add((op, len));
                    }

                    break;
                }
                case 0x03: // Section: 0x03 - String Table
                    // 3a) patch-local substring table
                    var tblCount = VarIntUtils.ReadVarInt<uint>(s);
                    patchStringTable = new List<string>(checked((int)tblCount));
                    for (var i = 0; i < tblCount; i++)
                    {
                        var byteLen = VarIntUtils.ReadVarInt<uint>(s);
                        var bytes = r.ReadBytes(checked((int)byteLen));
                        patchStringTable.Add(Encoding.UTF8.GetString(bytes));
                    }

                    // 3b) string-delta, values encoded via tokens using the patch-local table
                    var deltaCount = VarIntUtils.ReadVarInt<uint>(s);
                    for (var i = 0; i < deltaCount; i++)
                    {
                        var op = (PatchOperation)r.ReadByte();
                        var id = VarIntUtils.ReadVarInt<ushort>(s);
                        string? value = null;
                        if (op != PatchOperation.Remove)
                            value = ReadTokenizedString(r, patchStringTable); // tokens -> string
                        patch.StringTableDelta.Add(new PatchStringEntry(op, id, value));
                    }

                    break;
                case 0x04: // Section: 0x04 - Content IDs
                    var count = VarIntUtils.ReadVarInt<uint>(s);
                    contentIdEntries = new List<List<DeltaChunkRef>?>(checked((int)count));

                    var prev = 0UL;
                    for (var i = 0; i < count; i++)
                    {
                        var op = (PatchOperation)r.ReadByte();
                        var delta = VarIntUtils.ReadVarInt<ulong>(s);
                        var cid = prev + delta;
                        prev = cid;

                        List<DeltaChunkRef>? chunks = null;
                        if (op != PatchOperation.Remove)
                        {
                            chunks = ReadChunkRefs(r);
                            contentIdLookup[cid] = chunks;
                        }

                        contentIdEntries.Add(chunks);
                        patch.ContentIdDelta.Add(new PatchContentIdEntry(op, cid, chunks));
                    }

                    break;
                case 0x05: // Section: 0x05 - Components
                {
                    // ----- Component inserts -----
                    var compInsCount = VarIntUtils.ReadVarInt<uint>(s);
                    patch.ComponentInsert = new List<ComponentInsertPayload>(checked((int)compInsCount));
                    for (var i = 0; i < compInsCount; i++)
                    {
                        var compName = ReadTokenizedString(r, patchStringTable);
                        var ins = new ComponentInsertPayload { Name = compName };

                        var fileInsCount = VarIntUtils.ReadVarInt<uint>(s);
                        ins.Files = new List<FileInsertPayload>(checked((int)fileInsCount));

                        // common flags + bitmap
                        var commonFlags = r.ReadByte();
                        var overrides = ReadOverrideBitmap(r, s, (int)fileInsCount);

                        for (var j = 0; j < fileInsCount; j++)
                        {
                            var fname = ReadTokenizedString(r, patchStringTable);
                            var eff = overrides[j] ? r.ReadByte() : commonFlags;

                            ulong hash = 0;
                            if ((eff & FILE_HAS_HASH) != 0)
                            {
                                Span<byte> hb = stackalloc byte[8];
                                r.BaseStream.ReadExactly(hb);
                                hash = BinaryPrimitives.ReadUInt64LittleEndian(hb);
                            }

                            List<DeltaChunkRef> chunks;
                            switch (eff & FILE_CHUNK_MASK)
                            {
                                case FILE_CHUNK_KIND0:
                                    chunks = ReadChunkRefs(r);
                                    break;

                                case FILE_CHUNK_KIND1:
                                {
                                    var idx = VarIntUtils.ReadVarInt<uint>(s);
                                    if (idx >= contentIdEntries.Count || contentIdEntries[(int)idx] is null)
                                        throw new InvalidDataException($"Invalid ContentId index {idx} in component insert.");
                                    chunks = contentIdEntries[(int)idx]!;
                                    break;
                                }

                                case FILE_CHUNK_KIND2:
                                {
                                    Span<byte> cb = stackalloc byte[8];
                                    r.BaseStream.ReadExactly(cb);
                                    var literalCid = BinaryPrimitives.ReadUInt64LittleEndian(cb);
                                    if (!contentIdLookup.TryGetValue(literalCid, out var ch))
                                        throw new InvalidDataException($"Unknown literal ContentId 0x{literalCid:X16} in component insert.");
                                    chunks = ch;
                                    break;
                                }

                                default:
                                    throw new InvalidDataException("Unknown file chunk encoding kind.");
                            }

                            ins.Files.Add(new FileInsertPayload { Name = fname, Hash = hash, Chunks = chunks });
                        }

                        patch.ComponentInsert.Add(ins);
                    }

                    // ----- Component runs -----
                    var compRunCount = VarIntUtils.ReadVarInt<uint>(s);
                    patch.ComponentRuns = new List<(ListOp Op, uint Len)>(checked((int)compRunCount));
                    for (var i = 0; i < compRunCount; i++)
                    {
                        var op  = (ListOp)r.ReadByte();
                        var len = VarIntUtils.ReadVarInt<uint>(s);
                        patch.ComponentRuns.Add((op, len));
                    }

                    // ----- File scripts per component -----
                    var fileScriptCount = VarIntUtils.ReadVarInt<uint>(s);
                    patch.FileEdits = new Dictionary<string, FileListEdit>(checked((int)fileScriptCount), StringComparer.Ordinal);

                    for (var i = 0; i < fileScriptCount; i++)
                    {
                        var compName = ReadTokenizedString(r, patchStringTable);
                        var edit = new FileListEdit();

                        var fileInsCount = VarIntUtils.ReadVarInt<uint>(s);
                        edit.Insert = new List<FileInsertPayload>(checked((int)fileInsCount));

                        // common flags + bitmap
                        var common = r.ReadByte();
                        var overrides = ReadOverrideBitmap(r, s, (int)fileInsCount);

                        for (var j = 0; j < fileInsCount; j++)
                        {
                            var fname = ReadTokenizedString(r, patchStringTable);
                            var eff = overrides[j] ? r.ReadByte() : common;

                            ulong hash = 0;
                            if ((eff & FILE_HAS_HASH) != 0)
                            {
                                Span<byte> hb = stackalloc byte[8];
                                r.BaseStream.ReadExactly(hb);
                                hash = BinaryPrimitives.ReadUInt64LittleEndian(hb);
                            }

                            List<DeltaChunkRef> chunks;
                            switch (eff & FILE_CHUNK_MASK)
                            {
                                case FILE_CHUNK_KIND0:
                                    chunks = ReadChunkRefs(r);
                                    break;

                                case FILE_CHUNK_KIND1:
                                {
                                    var idx = VarIntUtils.ReadVarInt<uint>(s);
                                    if (idx >= contentIdEntries.Count || contentIdEntries[(int)idx] is null)
                                        throw new InvalidDataException($"Invalid ContentId index {idx} in file insert.");
                                    chunks = contentIdEntries[(int)idx]!;
                                    break;
                                }

                                case FILE_CHUNK_KIND2:
                                {
                                    Span<byte> cb = stackalloc byte[8];
                                    r.BaseStream.ReadExactly(cb);
                                    var literalCid = BinaryPrimitives.ReadUInt64LittleEndian(cb);
                                    if (!contentIdLookup.TryGetValue(literalCid, out var ch))
                                        throw new InvalidDataException($"Unknown literal ContentId 0x{literalCid:X16} in file insert.");
                                    chunks = ch;
                                    break;
                                }

                                default:
                                    throw new InvalidDataException("Unknown file chunk encoding kind.");
                            }

                            edit.Insert.Add(new FileInsertPayload { Name = fname, Hash = hash, Chunks = chunks });
                        }

                        // runs
                        var runCount = VarIntUtils.ReadVarInt<uint>(s);
                        edit.Runs = new List<(ListOp Op, uint Len)>(checked((int)runCount));
                        for (var j = 0; j < runCount; j++)
                        {
                            var op  = (ListOp)r.ReadByte();
                            var len = VarIntUtils.ReadVarInt<uint>(s);
                            edit.Runs.Add((op, len));
                        }

                        // modifies
                        var modCount = VarIntUtils.ReadVarInt<uint>(s);
                        edit.Modifies = new List<(string Name, byte[]? Hash, List<DeltaChunkRef>? Chunks)>(checked((int)modCount));
                        for (var j = 0; j < modCount; j++)
                        {
                            var name = ReadTokenizedString(r, patchStringTable);
                            var mflags = r.ReadByte();

                            byte[]? hash = null;
                            List<DeltaChunkRef>? chunks = null;
                            if ((mflags & 0x01) != 0) hash   = r.ReadBytes(8);
                            if ((mflags & 0x02) != 0) chunks = ReadChunkRefs(r);

                            edit.Modifies.Add((name, hash, chunks));
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
    
    private static ulong ComputeContentId(List<DeltaChunkRef> chunks)
    {
        // Same as your full serializer’s GetContentId
        var hasher = new XxHash3();
        Span<byte> buf = stackalloc byte[24];
        foreach (var c in chunks)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buf[..8],  c.DeltaIndex);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(8, 8),  c.Offset);
            BinaryPrimitives.WriteUInt64LittleEndian(buf.Slice(16, 8), c.Length);
            hasher.Append(buf);
        }
        return hasher.GetCurrentHashAsUInt64();
    }
    
    private static void SeedPatchStringTable(ReleasePackagePatch patch, SubstringTableBuilder substr)
    {
        // A) StringTableDelta values (Add/Modify only)
        foreach (var e in patch.StringTableDelta)
            if (e.Op != PatchOperation.Remove)
                _ = substr.Tokenize(e.Value ?? string.Empty);

        // B) Component insert payloads (names + files)
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
    
    // Pack/unpack override bitmaps
    private static void WriteOverrideBitmap(BinaryWriter w, IReadOnlyList<bool> diff)
    {
        // length (in bytes) to follow
        int n = diff.Count, bytes = (n + 7) >> 3;
        VarIntUtils.WriteVarInt(w, (uint)bytes);
        if (bytes == 0) return;
        Span<byte> buf = stackalloc byte[32];
        var tmp = bytes <= buf.Length ? buf.Slice(0, bytes) : new Span<byte>(new byte[bytes]);
        tmp.Clear();
        for (var i = 0; i < n; i++) if (diff[i]) tmp[i >> 3] |= (byte)(1 << (i & 7));
        w.Write(tmp);
    }

    private static bool[] ReadOverrideBitmap(BinaryReader r, Stream s, int count)
    {
        var bytes = (int)VarIntUtils.ReadVarInt<uint>(s);
        var res = new bool[count];
        if (bytes == 0) return res;
        var data = r.ReadBytes(bytes);
        for (var i = 0; i < count; i++)
            res[i] = (data[i >> 3] & (1 << (i & 7))) != 0;
        return res;
    }

}