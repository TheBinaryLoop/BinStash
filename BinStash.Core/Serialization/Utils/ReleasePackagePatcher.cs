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

using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using BinStash.Contracts.Release;
using BinStash.Core.Diffing;

namespace BinStash.Core.Serialization.Utils;

public static class ReleasePackagePatcher
{
    public static ReleasePackagePatch CreatePatch(ReleasePackage parent, ReleasePackage child, int level = 1, Action<string>? logger = null)
    {
        var patch = new ReleasePackagePatch
        {
            Version   = child.Version,
            ReleaseId = child.ReleaseId,
            RepoId    = child.RepoId,
            ParentId  = parent.ReleaseId,
            Level     = level,
            Notes     = child.Notes,
            CreatedAt = child.CreatedAt,
        };

        // 1) String table delta (value-set diff; ID = index within its own table)
        var parentSet = new HashSet<string>(parent.StringTable);
        var childSet  = new HashSet<string>(child.StringTable);
        
        logger?.Invoke($"Parent strings: {parentSet.Count}, Child strings: {childSet.Count}");

        foreach (var s in childSet)
            if (!parentSet.Contains(s))
                patch.StringTableDelta.Add(new PatchStringEntry(PatchOperation.Add,
                    (ushort)child.StringTable.IndexOf(s), s));

        foreach (var s in parentSet)
            if (!childSet.Contains(s))
                patch.StringTableDelta.Add(new PatchStringEntry(PatchOperation.Remove,
                    (ushort)parent.StringTable.IndexOf(s), null));

        // 2) Chunk table edit "script" (LCS-based diff; indices are for PARENT table)
        var chunkScript = ListEdit.Compute<byte[], byte[], string, byte[]>(
            parent: parent.Chunks.Select(x => x.Checksum).ToList(),
            child:  child.Chunks.Select(x => x.Checksum).ToList(),
            keyOfParent: Convert.ToHexStringLower,
            keyOfChild:  Convert.ToHexStringLower,
            buildInsert: h => h // insert payload is just the hash
        );
        patch.ChunkFinalCount = chunkScript.FinalCount;
        patch.ChunkInsertDict = chunkScript.Inserts;
        patch.ChunkRuns = chunkScript.Runs.Select(r => ((byte)r.Op, r.Len)).ToList();

        // Build child chunk index map (checksum -> index) so all refs we write use CHILD indices
        var childChunkIndex = BuildChunkIndex(child);

        // 3) ContentId mapping delta (aligned with serializer’s grouping + dedupe choice)
        var parentCidMap = BuildDedupeContentMap(parent); // only those that ShouldDeduplicate(parent)
        var childCidMap  = BuildDedupeContentMap(child);

        foreach (var (cid, childRefs) in childCidMap)
        {
            if (!parentCidMap.TryGetValue(cid, out var parentRefs))
            {
                patch.ContentIdDelta.Add(new PatchContentIdEntry(
                    PatchOperation.Add, cid, RemapRefsToChild(childRefs, childChunkIndex)));
            }
            else if (!ChunkRefsEqual(parentRefs, childRefs))
            {
                patch.ContentIdDelta.Add(new PatchContentIdEntry(
                    PatchOperation.Modify, cid, RemapRefsToChild(childRefs, childChunkIndex)));
            }
        }

        foreach (var cid in parentCidMap.Keys)
            if (!childCidMap.ContainsKey(cid))
                patch.ContentIdDelta.Add(new PatchContentIdEntry(PatchOperation.Remove, cid, null));

        // 4) Components / Files
        var componentScript = ListEdit.Compute<Component, Component, string, ComponentInsertPayload>(
            parent: parent.Components,
            child:  child.Components,
            keyOfParent: c => c.Name,
            keyOfChild:  c => c.Name,
            buildInsert: c => new ComponentInsertPayload
            {
                Name = c.Name,
                Files = c.Files.Select(f => new FileInsertPayload
                {
                    Name = f.Name,
                    Hash = f.Hash,
                    Chunks = RemapRefsToChild(f.Chunks, childChunkIndex)
                }).ToList()
            },
            keyComparer: StringComparer.Ordinal
        );
        
        patch.ComponentRuns = componentScript.Runs.Select(r => (r.Op, r.Len)).ToList();
        patch.ComponentInsert = componentScript.Inserts;
        
        // Build per-component **file** scripts for components that exist in both parent and child
        patch.FileEdits = new Dictionary<string, FileListEdit>(StringComparer.Ordinal);
        
        // Quick maps for existence/modifies
        var parentByName = parent.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var childByName  = child.Components.ToDictionary(c => c.Name,  StringComparer.Ordinal);
        
        // For each component that is present in **both** parent and child → compute a file script
        foreach (var (name, childComp) in childByName)
        {
            if (!parentByName.TryGetValue(name, out var parentComp))
                continue; // inserted components are covered by ComponentInsert payload (full files shipped)

            // File script (by file name)
            var fileScript = ListEdit.Compute<ReleaseFile, ReleaseFile, string, FileInsertPayload>(
                parent: parentComp.Files,
                child:  childComp.Files,
                keyOfParent: f => f.Name,
                keyOfChild:  f => f.Name,
                buildInsert: f => new FileInsertPayload
                {
                    Name   = f.Name,
                    Hash   = f.Hash,
                    Chunks = RemapRefsToChild(f.Chunks, childChunkIndex)
                },
                keyComparer: StringComparer.Ordinal
            );

            // Track modifies for files that are kept but changed hash/chunks
            var modifies = new List<(string Name, byte[]? Hash, List<DeltaChunkRef>? Chunks)>();
            var parentFilesByName = parentComp.Files.ToDictionary(f => f.Name, StringComparer.Ordinal);
            foreach (var kept in childComp.Files)
            {
                if (!parentFilesByName.TryGetValue(kept.Name, out var old)) continue; // not kept → insert payload handles it

                var hashChanged   = old.Hash != kept.Hash;
                var chunksChanged = !ChunkRefsEqual(old.Chunks, kept.Chunks);
                if (hashChanged || chunksChanged)
                {
                    modifies.Add((kept.Name,
                        hashChanged   ? UInt64ToBytesLE(kept.Hash) : null,
                        chunksChanged ? RemapRefsToChild(kept.Chunks, childChunkIndex) : null));
                }
            }

            if (fileScript.Runs.Count > 0 || fileScript.Inserts.Count > 0 || modifies.Count > 0)
            {
                patch.FileEdits[name] = new FileListEdit
                {
                    Runs     = fileScript.Runs.Select(r => ((ListOp)r.Op, r.Len)).ToList(),
                    Insert   = fileScript.Inserts,
                    Modifies = modifies
                };
            }
        }

        return patch;
    }

    public static ReleasePackage ApplyPatch(ReleasePackage baseRelease, ReleasePackagePatch patch)
    {
        // Start from base, then mutate to target
        var rp = DeepClone(baseRelease);

        // 1) String table (simple set/pos deltas)
        var table = rp.StringTable;
        foreach (var e in patch.StringTableDelta)
        {
            switch (e.Op)
            {
                case PatchOperation.Add:
                    EnsureIndex(table, e.Id);
                    if (e.Value is not null)
                    {
                        if (e.Id < table.Count) table[e.Id] = e.Value;
                        else table.Add(e.Value);
                    }
                    break;

                case PatchOperation.Remove:
                    if (e.Id < table.Count) table.RemoveAt(e.Id);
                    break;

                case PatchOperation.Modify:
                    EnsureIndex(table, e.Id);
                    table[e.Id] = e.Value ?? string.Empty;
                    break;
            }
        }

        // 2) Chunk table (order matters; indices used by refs below assume target indices)
        var parentHashes = rp.Chunks.Select(c => c.Checksum).ToList();
        var script = new EditScript<byte[]>
        {
            FinalCount = patch.ChunkFinalCount,
            Inserts = patch.ChunkInsertDict,
            Runs = patch.ChunkRuns.Select(r => new Run((ListOp)r.Op, r.Len)).ToList()
        };
        
        rp.Chunks = ListEdit.Apply(parentHashes, script, ins => ins).Select(h => new ChunkInfo(h)).ToList();
        
        // 3) ContentId mapping: store as auxiliary map (optional)
        // We don't need this to reconstruct files because file chunk refs are carried in ComponentDelta.
        // But we apply anyway so that building a patch on top of a patched release is consistent.
        var contentMap = BuildContentMap(rp);
        foreach (var e in patch.ContentIdDelta)
        {
            switch (e.Op)
            {
                case PatchOperation.Add:
                case PatchOperation.Modify:
                    contentMap[e.ContentId] = CloneRefs(e.Chunks ?? new());
                    break;
                case PatchOperation.Remove:
                    contentMap.Remove(e.ContentId);
                    break;
            }
        }

        // 4) Components & files
        if (patch.ComponentRuns is { Count: > 0 } || patch.ComponentInsert is { Count: > 0 })
        {
            var compScript = new EditScript<ComponentInsertPayload>
            {
                FinalCount = ComputeFinalCount(rp.Components.Count, patch.ComponentRuns),
                Inserts    = patch.ComponentInsert,
                Runs       = patch.ComponentRuns.Select(r => new Run(r.Op, r.Len)).ToList()
            };

            var applied = ListEdit.Apply(
                parent: rp.Components,
                script: compScript,
                fromInsert: ins => new Component
                {
                    Name  = ins.Name,
                    Files = ins.Files.Select(f => new ReleaseFile
                    {
                        Name   = f.Name,
                        Hash   = f.Hash,
                        Chunks = CloneRefs(f.Chunks)
                    }).ToList()
                }
            );

            rp.Components = applied;
        }
        
        if (patch.FileEdits is { Count: > 0 })
        {
            var compByName = rp.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);

            foreach (var (compName, edit) in patch.FileEdits)
            {
                if (!compByName.TryGetValue(compName, out var comp))
                    continue; // inserted comp’s files already set in insert payload

                // File list script
                if (edit.Runs is { Count: > 0 } || edit.Insert is { Count: > 0 })
                {
                    var fileScript = new EditScript<FileInsertPayload>
                    {
                        FinalCount = ComputeFinalCount(comp.Files.Count, edit.Runs)
                    };
                    fileScript.Runs.AddRange(edit.Runs.Select(r => new Run(r.Op, r.Len)));
                    fileScript.Inserts.AddRange(edit.Insert);

                    comp.Files = ListEdit.Apply(
                        parent: comp.Files,
                        script: fileScript,
                        fromInsert: ip => new ReleaseFile
                        {
                            Name   = ip.Name,
                            Hash   = ip.Hash,
                            Chunks = CloneRefs(ip.Chunks)
                        }
                    );
                }

                // Content modifies for kept files
                if (edit.Modifies is { Count: > 0 })
                {
                    var byName = comp.Files.ToDictionary(f => f.Name, StringComparer.Ordinal);
                    foreach (var (name, hash, chunks) in edit.Modifies)
                    {
                        if (!byName.TryGetValue(name, out var rf)) continue;
                        if (hash   is not null) rf.Hash   = BinaryPrimitives.ReadUInt64LittleEndian(hash);
                        if (chunks is not null) rf.Chunks = CloneRefs(chunks);
                    }
                }
            }
        }
        
        RebuildStringTable(rp);

        // 5) Header/meta + stats
        rp.Version = patch.Version;
        rp.ReleaseId = patch.ReleaseId;
        rp.RepoId = patch.RepoId;
        rp.Notes = patch.Notes;
        rp.CreatedAt = patch.CreatedAt;
        rp.Stats = RecomputeStats(rp);

        return rp;
    }

    public static ReleasePackage ApplyPatchChain(ReleasePackage baseRelease, IEnumerable<ReleasePackagePatch> patchesInOrder)
    {
        var cur = baseRelease;
        foreach (var p in patchesInOrder)
            cur = ApplyPatch(cur, p);
        return cur;
    }
    
    // ---- Helpers ------------------------------------------------------------

    #region Helpers

    private static ReleasePackage DeepClone(ReleasePackage rp) => new()
    {
        Version = rp.Version,
        ReleaseId = rp.ReleaseId,
        RepoId = rp.RepoId,
        Notes = rp.Notes,
        CreatedAt = rp.CreatedAt,
        CustomProperties = new Dictionary<string, string>(rp.CustomProperties),
        Chunks = rp.Chunks.Select(c => new ChunkInfo(c.Checksum.ToArray())).ToList(),
        StringTable = rp.StringTable.ToList(),
        Components = rp.Components.Select(c => new Component
        {
            Name = c.Name,
            Files = c.Files.Select(f => new ReleaseFile
            {
                Name = f.Name,
                Hash = f.Hash,
                Chunks = CloneRefs(f.Chunks)
            }).ToList()
        }).ToList(),
        Stats = new ReleaseStats
        {
            ComponentCount = rp.Stats.ComponentCount,
            FileCount      = rp.Stats.FileCount,
            ChunkCount     = rp.Stats.ChunkCount,
            RawSize        = rp.Stats.RawSize,
            DedupedSize    = rp.Stats.DedupedSize
        }
    };

    private static List<DeltaChunkRef> CloneRefs(List<DeltaChunkRef> r)
        => r.Select(x => new DeltaChunkRef(x.DeltaIndex, x.Offset, x.Length)).ToList();
    
    private static Dictionary<ulong, List<DeltaChunkRef>> BuildContentMap(ReleasePackage rp)
    {
        var map = new Dictionary<ulong, List<DeltaChunkRef>>();
        foreach (var c in rp.Components)
        foreach (var f in c.Files)
            map[f.Hash] = f.Chunks;
        return map;
    }

    private static void EnsureIndex(List<string> list, ushort id)
    {
        while (list.Count <= id) list.Add(string.Empty);
    }

    private static Dictionary<string, uint> BuildChunkIndex(ReleasePackage rp)
    {
        var map = new Dictionary<string, uint>(rp.Chunks.Count);
        for (var i = 0; i < rp.Chunks.Count; i++)
            map[Convert.ToHexStringLower(rp.Chunks[i].Checksum)] = (uint)i;
        return map;
    }

    private static List<DeltaChunkRef> RemapRefsToChild(List<DeltaChunkRef> refs, Dictionary<string,uint> childIndexByChecksum)
    {
        // In your full releases, DeltaIndex already encodes the index.
        // If those indices are for the CHILD (target) snapshot, this is a no-op.
        // If you ever change refs to carry checksum instead, this is the hook to translate.
        // Here: no translation needed; clone as-is.
        return refs.Select(r => new DeltaChunkRef(r.DeltaIndex, r.Offset, r.Length)).ToList();
    }

    private static bool ChunkRefsEqual(List<DeltaChunkRef> a, List<DeltaChunkRef> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.DeltaIndex != y.DeltaIndex || x.Offset != y.Offset || x.Length != y.Length) return false;
        }
        return true;
    }

    private static byte[] UInt64ToBytesLE(ulong v)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, v);
        return buf;
    }

    // --- ContentId grouping and dedupe decision (mirrors your serializer) ----

    private static ulong GetContentId(List<DeltaChunkRef> chunks)
    {
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

    private readonly record struct PackedStats(byte BitsDelta, byte BitsOffset, byte BitsLength, int PackedLength);

    private static Dictionary<ulong, List<DeltaChunkRef>> BuildDedupeContentMap(ReleasePackage rp)
    {
        // Group identical chunk lists -> occurrences
        var map = new Dictionary<ulong, (List<DeltaChunkRef> Refs, PackedStats Stats, int Occ)>();

        foreach (var comp in rp.Components)
        foreach (var file in comp.Files)
        {
            var refs = file.Chunks;
            var cid = GetContentId(refs);

            if (map.TryGetValue(cid, out var ex))
            {
                map[cid] = (ex.Refs, ex.Stats, ex.Occ + 1);
                continue;
            }

            // Compute packing stats same way as serializer
            byte bitsDelta, bitsOffset, bitsLength; int packedLen;
            if (refs.Count == 0)
            {
                bitsDelta = bitsOffset = bitsLength = 0;
                packedLen = 0;
            }
            else
            {
                static byte BitsU64(ulong max) => max == 0 ? (byte)0 : (byte)(BitOperations.Log2(max) + 1);
                var maxDelta  = refs.Max(c => (ulong)c.DeltaIndex);
                var maxOffset = refs.Max(c => c.Offset);
                var maxLength = refs.Max(c => c.Length);
                bitsDelta  = BitsU64(maxDelta);
                bitsOffset = BitsU64(maxOffset);
                bitsLength = BitsU64(maxLength);

                // We only need the packed length to evaluate the heuristic; compute it cheaply:
                var totalBits = refs.Count * (bitsDelta + bitsOffset + bitsLength);
                packedLen = ((totalBits + 7) >> 1 >> 2); // divide by 8 without overflow
            }

            map[cid] = (refs, new PackedStats(bitsDelta, bitsOffset, bitsLength, packedLen), 1);
        }

        // Keep only entries where serializer would dedupe
        var result = new Dictionary<ulong, List<DeltaChunkRef>>();
        foreach (var (cid, entry) in map)
        {
            if (ShouldDeduplicate(entry.Stats, entry.Occ))
                result[cid] = entry.Refs;
        }
        return result;
    }

    private static bool ShouldDeduplicate(PackedStats stats, int occurrences)
    {
        // Mirrors your ShouldDeduplicate math:
        // inlineSizePerFile = VarInt(count) + 3 + VarInt(packedLen) + packedLen
        // totalInline = (inlineSizePerFile + 1) * occurrences
        // sharedBlock = VarInt(count) + 3 + VarInt(packedLen) + packedLen
        // totalDedupe = sharedBlock + (1 + VarInt(occ-1)) * occurrences

        var count = EstimateChunkCount(stats.PackedLength, stats.BitsDelta, stats.BitsOffset, stats.BitsLength);
        var inlinePer =
            VarIntUtils.VarIntSize((uint)count) + 3 + VarIntUtils.VarIntSize((uint)stats.PackedLength) + stats.PackedLength;

        var totalInline = (inlinePer + 1) * occurrences;

        var shared =
            VarIntUtils.VarIntSize((uint)count) + 3 + VarIntUtils.VarIntSize((uint)stats.PackedLength) + stats.PackedLength;

        var refCost = VarIntUtils.VarIntSize((uint)Math.Max(occurrences - 1, 0));
        var totalDedupe = shared + (1 + refCost) * occurrences;

        return totalDedupe < totalInline;
    }

    private static int EstimateChunkCount(int packedBytes, byte bDelta, byte bOff, byte bLen)
    {
        var bitsPer = bDelta + bOff + bLen;
        if (bitsPer == 0) return 0;
        var totalBits = packedBytes * 8;
        return totalBits / bitsPer;
    }
    
    private static ReleaseStats RecomputeStats(ReleasePackage rp)
    {
        var stats = new ReleaseStats
        {
            ComponentCount = (uint)rp.Components.Count,
            FileCount      = (uint)rp.Components.Sum(c => c.Files.Count),
            ChunkCount     = (uint)rp.Chunks.Count
        };

        ulong raw = 0;
        foreach (var c in rp.Components)
        foreach (var f in c.Files)
        foreach (var ch in f.Chunks)
            raw += ch.Length;

        stats.RawSize = raw;
        stats.DedupedSize = raw; // refine if you track physical chunk sizes
        return stats;
    }
    
    private static void RebuildStringTable(ReleasePackage rp)
    {
        var sb = new SubstringTableBuilder();

        // Keep the same traversal pattern as your serializer
        foreach (var comp in rp.Components)
        {
            _ = sb.Tokenize(comp.Name);
            foreach (var f in comp.Files)
                _ = sb.Tokenize(f.Name);
        }

        foreach (var kv in rp.CustomProperties)
        {
            _ = sb.Tokenize(kv.Key);
            _ = sb.Tokenize(kv.Value);
        }

        rp.StringTable = sb.Table;
    }

    private static int ComputeFinalCount(int parentCount, IReadOnlyList<(ListOp Op, uint Len)> runs)
    {
        long final = parentCount;
        foreach (var (op, len) in runs)
        {
            switch (op)
            {
                case ListOp.Keep: /* no change */ break;
                case ListOp.Del:  final -= len;   break;
                case ListOp.Ins:  final += len;   break;
            }
        }
        if (final < 0 || final > int.MaxValue)
            throw new InvalidOperationException($"Computed final count out of range: {final}");
        return (int)final;
    }

    
    #endregion

}