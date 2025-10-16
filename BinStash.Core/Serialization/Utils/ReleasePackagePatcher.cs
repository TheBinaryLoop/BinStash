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

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Diffing;

namespace BinStash.Core.Serialization.Utils;

public static class ReleasePackagePatcher
{
    /// <summary>
    /// Create a patch between two v2 ReleasePackages, aligned with the new serializer:
    /// - StringTableDelta is a set-diff (IDs are indices in each package's table).
    /// - FileHash* is an edit script over the ordered unique file-hash lists
    ///   (order = frequency desc, then hash bytes asc) – exactly how the v2 serializer builds its map.
    /// - Components/Files only carry Name + Hash32. Modifies only sets Hash when changed.
    /// - ContentIdDelta is not used in v2 (left empty).
    /// </summary>
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
                patch.StringTableDelta.Add(new PatchStringEntry(
                    PatchOperation.Add,
                    (ushort)child.StringTable.IndexOf(s),
                    s));

        foreach (var s in parentSet)
            if (!childSet.Contains(s))
                patch.StringTableDelta.Add(new PatchStringEntry(
                    PatchOperation.Remove,
                    (ushort)parent.StringTable.IndexOf(s),
                    null));

        // 2) File hash dictionary edit script (v2: unique file hashes, ordered by freq desc then bytes)
        var parentHashList = BuildOrderedFileHashList(parent); // List<byte[32]>
        var childHashList  = BuildOrderedFileHashList(child);  // List<byte[32]>

        var hashScript = ListEdit.Compute<byte[], byte[], string, byte[]>(
            parent: parentHashList,
            child:  childHashList,
            keyOfParent: Convert.ToHexStringLower,
            keyOfChild:  Convert.ToHexStringLower,
            buildInsert: h => h
        );

        patch.FileHashFinalCount  = hashScript.FinalCount;
        patch.FileHashInsertDict  = hashScript.Inserts;
        patch.FileHashRuns        = hashScript.Runs.Select(r => ((byte)r.Op, r.Len)).ToList();

        // 3) Components / Files (name + Hash32 only)
        var componentScript = ListEdit.Compute<Component, Component, string, ComponentInsertPayload>(
            parent: parent.Components,
            child:  child.Components,
            keyOfParent: c => c.Name,
            keyOfChild:  c => c.Name,
            buildInsert: c => new ComponentInsertPayload
            {
                Name  = c.Name,
                Files = c.Files.Select(f => new FileInsertPayload
                {
                    Name = f.Name,
                    Hash = f.Hash,
                    Chunks = [] // not used in v2
                }).ToList()
            },
            keyComparer: StringComparer.Ordinal
        );

        patch.ComponentRuns   = componentScript.Runs.Select(r => (r.Op, r.Len)).ToList();
        patch.ComponentInsert = componentScript.Inserts;

        // Per-component file scripts for components present in both
        patch.FileEdits = new Dictionary<string, FileListEdit>(StringComparer.Ordinal);

        var parentByName = parent.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var childByName  = child.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);

        foreach (var (name, childComp) in childByName)
        {
            if (!parentByName.TryGetValue(name, out var parentComp))
                continue; // inserted components handled above

            var fileScript = ListEdit.Compute<ReleaseFile, ReleaseFile, string, FileInsertPayload>(
                parent: parentComp.Files,
                child:  childComp.Files,
                keyOfParent: f => f.Name,
                keyOfChild:  f => f.Name,
                buildInsert: f => new FileInsertPayload
                {
                    Name   = f.Name,
                    Hash   = f.Hash,
                    Chunks = [] // not used in v2
                },
                keyComparer: StringComparer.Ordinal
            );

            // Kept & modified files (hash change only)
            var modifies = new List<(string Name, Hash32? Hash, List<DeltaChunkRef>? Chunks)>();
            var parentFilesByName = parentComp.Files.ToDictionary(f => f.Name, StringComparer.Ordinal);

            foreach (var keptChild in childComp.Files)
            {
                if (!parentFilesByName.TryGetValue(keptChild.Name, out var old)) continue;
                var hashChanged = old.Hash != keptChild.Hash;
                if (hashChanged)
                    modifies.Add((keptChild.Name, keptChild.Hash, null));
            }

            if (fileScript.Runs.Count > 0 || fileScript.Inserts.Count > 0 || modifies.Count > 0)
            {
                patch.FileEdits[name] = new FileListEdit
                {
                    Runs     = fileScript.Runs.Select(r => (r.Op, r.Len)).ToList(),
                    Insert   = fileScript.Inserts,
                    Modifies = modifies
                };
            }
        }
        
        var pk = parent.CustomProperties;
        var ck = child.CustomProperties;

        // Adds & Modifies
        foreach (var (key, childVal) in ck)
        {
            if (!pk.TryGetValue(key, out var parentVal))
            {
                patch.CustomPropertiesDelta.Add(new PatchPropertyEntry(PatchOperation.Add, key, childVal));
            }
            else if (!StringComparer.Ordinal.Equals(parentVal, childVal))
            {
                patch.CustomPropertiesDelta.Add(new PatchPropertyEntry(PatchOperation.Modify, key, childVal));
            }
        }

        // Removes
        foreach (var key in pk.Keys)
            if (!ck.ContainsKey(key))
                patch.CustomPropertiesDelta.Add(new PatchPropertyEntry(PatchOperation.Remove, key, null));

        return patch;
    }
    
    public static ReleasePackage ApplyPatch(ReleasePackage baseRelease, ReleasePackagePatch patch)
    {
        // 0) clone so we don't mutate the caller's instance
        var rp = DeepClone(baseRelease);

        // 1) Apply CustomProperties delta FIRST (affects tokens)
        if (patch.CustomPropertiesDelta.Count > 0)
        {
            foreach (var e in patch.CustomPropertiesDelta)
            {
                switch (e.Op)
                {
                    case PatchOperation.Add:
                    case PatchOperation.Modify:
                        rp.CustomProperties[e.Key] = e.Value ?? string.Empty;
                        break;
                    case PatchOperation.Remove:
                        rp.CustomProperties.Remove(e.Key);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown property op: {e.Op}");
                }
            }
        }

        // 2) Apply string-table delta next (IDs are table-local), then rebuild from content
        ApplyStringTableDelta(rp.StringTable, patch.StringTableDelta);

        // 3) Components (runs + inserts)
        if (patch.ComponentRuns.Count > 0 || patch.ComponentInsert.Count > 0)
        {
            var compScript = new EditScript<ComponentInsertPayload>
            {
                FinalCount = ComputeFinalCount(rp.Components.Count, patch.ComponentRuns),
                Runs       = patch.ComponentRuns.Select(r => new Run(r.Op, r.Len)).ToList(),
                Inserts    = patch.ComponentInsert
            };

            rp.Components = ListEdit.Apply(
                parent: rp.Components,
                script: compScript,
                fromInsert: ins => new Component
                {
                    Name  = ins.Name,
                    Files = ins.Files.Select(f => new ReleaseFile
                    {
                        Name   = f.Name,
                        Hash   = f.Hash,
                        // v2 doesn’t use chunks, keep empty or preserve default
                        Chunks = new List<DeltaChunkRef>()
                    }).ToList()
                }
            );
        }

        // 4) Per-component file scripts (for kept components)
        if (patch.FileEdits.Count > 0)
        {
            var compByName = rp.Components.ToDictionary(c => c.Name, StringComparer.Ordinal);
            foreach (var (compName, edit) in patch.FileEdits)
            {
                if (!compByName.TryGetValue(compName, out var comp))
                    continue; // inserted components were handled above

                // 3a) apply file list runs + inserts
                if (edit.Runs.Count > 0 || edit.Insert.Count > 0)
                {
                    var fileScript = new EditScript<FileInsertPayload>
                    {
                        FinalCount = ComputeFinalCount(comp.Files.Count, edit.Runs),
                        Runs       = edit.Runs.Select(r => new Run(r.Op, r.Len)).ToList(),
                        Inserts    = edit.Insert
                    };

                    comp.Files = ListEdit.Apply(
                        parent: comp.Files,
                        script: fileScript,
                        fromInsert: ip => new ReleaseFile
                        {
                            Name   = ip.Name,
                            Hash   = ip.Hash,
                            Chunks = new List<DeltaChunkRef>()
                        }
                    );
                }

                // 3b) apply modifies (hash only in v2)
                if (edit.Modifies.Count > 0)
                {
                    var byName = comp.Files.ToDictionary(f => f.Name, StringComparer.Ordinal);
                    foreach (var (name, hash, /*chunks*/ _) in edit.Modifies)
                    {
                        if (!byName.TryGetValue(name, out var rf)) continue;
                        if (hash is { } h) rf.Hash = h;
                    }
                }
            }
        }

        // 4) File-hash dictionary edit script is not required to realize the child in-memory model.
        //    (Your v2 serializer recomputes it during serialization.) Safe to ignore here.

        // 5) Now that content & properties are final, rebuild the token table to mirror the serializer
        RebuildStringTable(rp);

        // 6) Header/meta + simple stats
        rp.Version   = patch.Version;
        rp.ReleaseId = patch.ReleaseId;
        rp.RepoId    = patch.RepoId;
        rp.Notes     = patch.Notes;
        rp.CreatedAt = patch.CreatedAt;
        rp.Stats     = RecomputeStats(rp);

        return rp;
    }

    public static ReleasePackage ApplyPatchChain(ReleasePackage baseRelease, IEnumerable<ReleasePackagePatch> patchesInOrder)
    {
        var cur = baseRelease;
        foreach (var p in patchesInOrder)
            cur = ApplyPatch(cur, p);
        return cur;
    }

    // --- helpers ---

    private static ReleasePackage DeepClone(ReleasePackage rp) => new()
    {
        Version        = rp.Version,
        ReleaseId      = rp.ReleaseId,
        RepoId         = rp.RepoId,
        Notes          = rp.Notes,
        CreatedAt      = rp.CreatedAt,
        CustomProperties = new Dictionary<string, string>(rp.CustomProperties),
        // keep chunk metadata if present (v2 doesn’t need it, but don’t lose it)
        Chunks         = rp.Chunks.Select(c => new ChunkInfo(c.Checksum.ToArray())).ToList(),
        StringTable    = rp.StringTable.ToList(),
        Components     = rp.Components.Select(c => new Component
        {
            Name  = c.Name,
            Files = c.Files.Select(f => new ReleaseFile
            {
                Name   = f.Name,
                Hash   = f.Hash,
                Chunks = f.Chunks.Select(x => new DeltaChunkRef(x.DeltaIndex, x.Offset, x.Length)).ToList()
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

    private static void ApplyStringTableDelta(List<string> table, List<PatchStringEntry> delta)
    {
        foreach (var e in delta)
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

                default:
                    throw new InvalidOperationException($"Unknown string-delta op: {e.Op}");
            }
        }

        static void EnsureIndex(List<string> list, ushort id)
        {
            while (list.Count <= id) list.Add(string.Empty);
        }
    }

    private static void RebuildStringTable(ReleasePackage rp)
    {
        // mirror the v2 serializer’s tokenization traversal (components/files + custom props)
        var sb = new SubstringTableBuilder();

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
                case ListOp.Keep: break;
                case ListOp.Del:  final -= len; break;
                case ListOp.Ins:  final += len; break;
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }
        if (final < 0 || final > int.MaxValue)
            throw new InvalidOperationException($"Computed final count out of range: {final}");
        return (int)final;
    }

    private static ReleaseStats RecomputeStats(ReleasePackage rp)
    {
        // v2: we can’t infer byte sizes without chunk lengths; keep counts accurate; preserve prior sizes if you prefer.
        var stats = rp.Stats;
        stats.ComponentCount = (uint)rp.Components.Count;
        stats.FileCount      = (uint)rp.Components.Sum(c => c.Files.Count);
        stats.ChunkCount     = (uint)rp.Chunks.Count;

        // leave RawSize/DedupedSize unchanged (or set to 0 if you want a hard reset)
        return stats;
    }
    
    private static List<byte[]> BuildOrderedFileHashList(ReleasePackage rp)
    {
        // Count occurrences of Hash32 across all files
        var freq = new Dictionary<Hash32, int>();
        foreach (var f in rp.Components.SelectMany(c => c.Files))
            freq[f.Hash] = freq.TryGetValue(f.Hash, out var n) ? n + 1 : 1;

        // Order by frequency desc, then by hash bytes asc (exactly like the v2 serializer)
        var ordered = freq.Keys
            .OrderByDescending(h => freq[h])
            .ThenBy(h => h)
            .Select(h => h.GetBytes()) // byte[32]
            .ToList();

        return ordered;
    }

}