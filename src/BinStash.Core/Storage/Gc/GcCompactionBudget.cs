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

namespace BinStash.Core.Storage.Gc;

/// <summary>
/// How much rewriting one garbage-collection run may do, shared across every bucket it touches.
/// </summary>
/// <remarks>
/// <para>
/// Compaction is copy-forward: the survivors of a pack are written to a new file and the original
/// is only unlinked once its drain window has passed. The two therefore coexist, so a run's peak
/// extra space is the size of everything it chose to rewrite. Capping that per bucket is no cap
/// at all — a store has thousands of buckets and each holds only a handful of packs, so a run
/// compacts all of them and the peak scales with the store. On a terabyte store that is a
/// terabyte of headroom nobody has.
/// </para>
/// <para>
/// A run-wide budget makes the peak a constant the operator chooses rather than a function of
/// how much garbage happens to have accumulated. What does not fit stays tombstoned and is
/// picked up by the next run, so the work still completes — it just arrives in instalments.
/// </para>
/// </remarks>
public sealed class GcCompactionBudget
{
    private readonly object _gate = new();
    private long _remainingBytes;
    private int _remainingPacks;

    /// <summary>An unrestricted budget, for callers that impose their own bound.</summary>
    public static GcCompactionBudget Unlimited { get; } = new(long.MaxValue, int.MaxValue);

    public GcCompactionBudget(long bytes, int packs)
    {
        _remainingBytes = Math.Max(0, bytes);
        _remainingPacks = Math.Max(0, packs);
        TotalBytes = _remainingBytes;
    }

    /// <summary>What the budget started with, for reporting.</summary>
    public long TotalBytes { get; }

    /// <summary>Bytes reserved by compaction so far.</summary>
    public long ReservedBytes
    {
        get { lock (_gate) return TotalBytes == long.MaxValue ? 0 : TotalBytes - _remainingBytes; }
    }

    /// <summary>Whether anything at all is left to spend.</summary>
    public bool IsExhausted
    {
        get { lock (_gate) return _remainingBytes <= 0 || _remainingPacks <= 0; }
    }

    /// <summary>
    /// Claims room for one more source pack. Returns <see langword="false"/> when the run has
    /// spent its allowance, in which case the caller must leave the pack for a later run.
    /// </summary>
    /// <remarks>
    /// A pack larger than the whole budget would otherwise never be collectable, so the first
    /// reservation of a run is always granted — a budget that cannot make progress is worse than
    /// one briefly overspent.
    /// </remarks>
    public bool TryReserve(long bytes)
    {
        lock (_gate)
        {
            if (_remainingPacks <= 0)
                return false;

            var untouched = _remainingBytes == TotalBytes;

            if (bytes > _remainingBytes && !untouched)
                return false;

            _remainingBytes -= bytes;
            _remainingPacks--;
            return true;
        }
    }
}
