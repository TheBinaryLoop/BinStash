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

using BinStash.Contracts.Release;

namespace BinStash.Core.Diffing;

public readonly record struct Run(ListOp Op, uint Len);

public sealed class EditScript<TInsert>
{
    public List<Run> Runs { get; init; } = new();
    public List<TInsert> Inserts { get; init;  } = new();
    public int FinalCount { get; init; } // child count
}

public static class ListEdit
{
    /// <summary>
    /// Compute a compact edit script (keep/delete/insert + insert payloads) to transform
    /// the parent list into the child list. Keys must be unique in both lists.
    /// </summary>
    /// <typeparam name="TParent">Parent item type</typeparam>
    /// <typeparam name="TChild">Child item type</typeparam>
    /// <typeparam name="TKey">Key type (unique, equatable)</typeparam>
    /// <typeparam name="TInsert">Insert payload type (what you want to serialize for new items)</typeparam>
    /// <param name="parent">Parent list</param>
    /// <param name="child">Child list</param>
    /// <param name="keyOfParent">Key selector for parent items</param>
    /// <param name="keyOfChild">Key selector for child items</param>
    /// <param name="buildInsert">Builds the insert payload from a CHILD item</param>
    /// <param name="keyComparer">Key equality comparer (optional)</param>
    public static EditScript<TInsert> Compute<TParent, TChild, TKey, TInsert>(
        IReadOnlyList<TParent> parent,
        IReadOnlyList<TChild> child,
        Func<TParent, TKey> keyOfParent,
        Func<TChild, TKey> keyOfChild,
        Func<TChild, TInsert> buildInsert,
        IEqualityComparer<TKey>? keyComparer = null) where TKey : notnull
    {
        keyComparer ??= EqualityComparer<TKey>.Default;

        // 1) Build parent key -> index map
        var pIndex = new Dictionary<TKey, int>(parent.Count, keyComparer);
        for (var i = 0; i < parent.Count; i++)
        {
            var k = keyOfParent(parent[i]);
            pIndex.TryAdd(k, i); // ignore duplicates if any (keep first)
        }

        // 2) For each child item, record the parent index if it exists; build sequence for LIS
        var childPosToParentIdx = new int[child.Count];
        Array.Fill(childPosToParentIdx, -1);

        var parentIdxSeq = new List<int>(child.Count);
        var childPosForSeq = new List<int>(child.Count); // parallel to parentIdxSeq, stores child positions

        for (var c = 0; c < child.Count; c++)
        {
            var k = keyOfChild(child[c]);
            if (pIndex.TryGetValue(k, out var pi))
            {
                childPosToParentIdx[c] = pi;
                parentIdxSeq.Add(pi);
                childPosForSeq.Add(c);
            }
        }

        // 3) LIS over parent indices => LCS of keys (since indices must increase to preserve order)
        var lisIdx = LongestIncreasingSubsequence(parentIdxSeq); // returns indices into parentIdxSeq
        var keepPairs = new List<(int pIdx, int cIdx)>(lisIdx.Count);
        foreach (var i in lisIdx)
            keepPairs.Add((parentIdxSeq[i], childPosForSeq[i]));

        // 4) Build set of "kept" child positions for insert payload collection
        var keepChild = new HashSet<int>(keepPairs.Select(x => x.cIdx));

        // 5) Insert payload = child items that are NOT kept, in child order
        var script = new EditScript<TInsert> { FinalCount = child.Count };
        for (var c = 0; c < child.Count; c++)
            if (!keepChild.Contains(c))
                script.Inserts.Add(buildInsert(child[c]));

        // 6) Emit runs by walking parent & child in lockstep through keep anchors
        var cursorInParent = 0;
        var cursorInChild = 0;

        void Emit(ListOp op, int len)
        {
            if (len <= 0) return;
            var ulen = (uint)len;
            // coalesce same-op runs
            if (script.Runs.Count > 0 && script.Runs[^1].Op == op)
            {
                var prev = script.Runs[^1];
                script.Runs[^1] = new Run(op, prev.Len + ulen);
            }
            else script.Runs.Add(new Run(op, ulen));
        }

        // keepPairs are sorted by child order because lisIdx is increasing in sequence order
        foreach (var (kpParent, kpChild) in keepPairs)
        {
            // Delete any parent items before the next kept parent index
            Emit(ListOp.Del, kpParent - cursorInParent);
            cursorInParent = kpParent;

            // Insert any child items before the next kept child index
            Emit(ListOp.Ins, kpChild - cursorInChild);
            cursorInChild = kpChild;

            // Keep this one item (advance both)
            Emit(ListOp.Keep, 1);
            cursorInParent    += 1;
            cursorInChild += 1;
        }

        // Tail: delete remaining parent, insert remaining child
        Emit(ListOp.Del, parent.Count - cursorInParent);
        Emit(ListOp.Ins, child.Count  - cursorInChild);

        return script;
    }

    /// <summary>
    /// Apply an edit script to a parent list to produce the child list.
    /// </summary>
    /// <typeparam name="TOut">Output item type (usually the same as input type)</typeparam>
    /// <typeparam name="TInsert">Insert payload type</typeparam>
    /// <param name="parent">Parent list</param>
    /// <param name="script">Script with runs and inserts</param>
    /// <param name="fromInsert">Factory: construct an output item from an insert payload</param>
    public static List<TOut> Apply<TOut, TInsert>(
        IReadOnlyList<TOut> parent,
        EditScript<TInsert> script,
        Func<TInsert, TOut> fromInsert)
    {
        var result = new List<TOut>(Math.Max(script.FinalCount, parent.Count));
        var cursorInParent = 0;
        var insertPayloadCursor = 0;

        foreach (var (op, len) in script.Runs)
        {
            switch (op)
            {
                case ListOp.Keep:
                    for (uint i = 0; i < len; i++) result.Add(parent[cursorInParent++]);
                    break;

                case ListOp.Del:
                    cursorInParent += (int)len; // skip
                    break;

                case ListOp.Ins:
                    for (uint i = 0; i < len; i++) result.Add(fromInsert(script.Inserts[insertPayloadCursor++]));
                    break;
            }
        }

        if (result.Count != script.FinalCount)
            throw new InvalidOperationException($"Edit script produced {result.Count} items, expected {script.FinalCount}.");

        return result;
    }

    // ---------- helpers ----------

    // Classic patience/LIS O(n log n) for strictly increasing sequence.
    private static List<int> LongestIncreasingSubsequence(List<int> a)
    {
        var n = a.Count;
        var parent = new int[n];
        var tails  = new int[n];
        var size = 0;

        for (var i = 0; i < n; i++)
        {
            var x = a[i];
            int l = 0, r = size;
            while (l < r)
            {
                var m = (l + r) >> 1;
                if (a[tails[m]] < x) l = m + 1; else r = m;
            }
            parent[i] = (l > 0) ? tails[l - 1] : -1;
            tails[l]  = i;
            if (l == size) size++;
        }

        var lis = new List<int>(size);
        for (var i = tails[size - 1]; i >= 0; i = parent[i]) lis.Add(i);
        lis.Reverse();
        return lis;
    }
}