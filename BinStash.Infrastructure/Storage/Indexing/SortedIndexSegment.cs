// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using BinStash.Contracts.Hashing;

namespace BinStash.Infrastructure.Storage.Indexing;

/// <summary>
/// An immutable, sorted, memory-mapped index segment file.
///
/// <para>
/// <strong>File format (<c>seg-NNN.idx</c>):</strong>
/// <code>
/// Header (8 bytes):
///   [0..3]  Magic      : uint32 LE = 0x58324449  ('I','D','X','2')
///   [4..7]  EntryCount : uint32 LE
///
/// Entry table: EntryCount × 48 bytes (sorted ascending by Hash):
///   [ 0..31] Hash   : 32 bytes BLAKE3  (four LE uint64 = Hash32 wire format)
///   [32..35] FileNo : uint32 LE
///   [36..43] Offset : uint64 LE
///   [44..47] Length : uint32 LE
/// </code>
/// Fixed record width → entry_offset = 8 + i × 48 → O(1) seek, O(log n) binary search.
/// </para>
///
/// <para>
/// <strong>Sort order:</strong> Ascending by <see cref="Hash32.CompareTo"/>, which
/// compares the four internal LE uint64 fields lexicographically (_h0 first).
/// This is identical to comparing the 32 raw bytes as LE uint64 words.
/// </para>
///
/// <para>
/// <strong>Compaction level encoding in filename:</strong>
/// <list type="bullet">
///   <item><c>seg-0NN.idx</c> — Level 0: ≤ 65,536 entries   (flushed from log)</item>
///   <item><c>seg-1NN.idx</c> — Level 1: ≤ 1,048,576 entries (merge of 16 Level-0 segs)</item>
///   <item><c>seg-2NN.idx</c> — Level 2: ≤ 16,777,216 entries (merge of 16 Level-1 segs)</item>
/// </list>
/// </para>
/// </summary>
internal sealed class SortedIndexSegment : IDisposable
{
    // -----------------------------------------------------------------------
    // Format constants

    internal const uint Magic = 0x58324449; // 'I','D','X','2' little-endian
    internal const int HeaderSize = 8;
    internal const int EntrySize = 48;

    // Level capacity thresholds (inclusive upper bound on entry count).
    internal const int Level0MaxEntries = 65_536;
    internal const int Level1MaxEntries = 1_048_576;
    internal const int Level2MaxEntries = 16_777_216;

    // -----------------------------------------------------------------------
    // Instance state

    private readonly FileStream _fileStream; // kept open to hold FileShare.Delete
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;

    /// <summary>Number of entries stored in this segment.</summary>
    public int EntryCount { get; }

    // -----------------------------------------------------------------------
    // Construction / opening

    /// <summary>
    /// Opens an existing segment file for reading.
    /// </summary>
    /// <remarks>
    /// The underlying file is opened with <see cref="FileShare.ReadWrite"/> |
    /// <see cref="FileShare.Delete"/> so that on Windows the file can be
    /// atomically replaced (via <c>MoveFileExW</c> with
    /// <c>MOVEFILE_REPLACE_EXISTING</c>) or deleted while this segment is
    /// still mapped.  The memory-mapped view continues to reference the old
    /// inode data until this instance is disposed.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// Thrown if the file header is corrupt or the file is too short.
    /// </exception>
    public SortedIndexSegment(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists)
            throw new FileNotFoundException($"Segment file not found: {path}", path);

        if (fi.Length < HeaderSize)
            throw new InvalidDataException($"Segment file too short: {path}");

        // Open with FileShare.Delete so that Windows allows File.Move(overwrite:true)
        // and File.Delete to succeed even while this MMF is still mapped.
        _fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            options: FileOptions.RandomAccess);

        _mmf = MemoryMappedFile.CreateFromFile(
            _fileStream,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read,
            HandleInheritability.None,
            leaveOpen: false); // MMF owns the stream now; disposes it on MMF.Dispose()

        _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        // Validate header
        var magic = _view.ReadUInt32(0);
        if (magic != Magic)
        {
            _view.Dispose();
            _mmf.Dispose();
            throw new InvalidDataException(
                $"Segment file has wrong magic 0x{magic:X8} (expected 0x{Magic:X8}): {path}");
        }

        EntryCount = (int)_view.ReadUInt32(4);

        var expectedLength = HeaderSize + (long)EntryCount * EntrySize;
        if (fi.Length < expectedLength)
        {
            _view.Dispose();
            _mmf.Dispose();
            throw new InvalidDataException($"Segment file truncated: {path}. Expected {expectedLength} bytes, got {fi.Length}.");
        }
    }

    // -----------------------------------------------------------------------
    // Lookup

    /// <summary>
    /// Binary-searches the segment for <paramref name="hash"/>.
    /// Returns the matching entry, or <see langword="null"/> if not found.
    /// Zero heap allocations on the hot path.
    /// </summary>
    public IndexEntry? TryFind(Hash32 hash)
    {
        if (EntryCount == 0)
            return null;

        var lo = 0;
        var hi = EntryCount - 1;

        Span<byte> buf = stackalloc byte[32];

        while (lo <= hi)
        {
            var mid = (int)(((uint)lo + (uint)hi) >> 1);
            var offset = HeaderSize + (long)mid * EntrySize;

            ReadHashAt(offset, buf);
            var candidate = new Hash32(buf);

            var cmp = hash.CompareTo(candidate);
            if (cmp == 0)
            {
                // Found — read the rest of the entry
                var fileNo  = (int) _view.ReadUInt32(offset + 32);
                var dataOff = _view.ReadInt64(offset + 36);
                var length  = (int) _view.ReadUInt32(offset + 44);
                return new IndexEntry(fileNo, dataOff, length);
            }

            if (cmp < 0)
                hi = mid - 1;
            else
                lo = mid + 1;
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Stats (header-only — no open accessor required beyond construction)

    /// <summary>
    /// Reads the entry count from the 8-byte header of a segment file
    /// <em>without</em> opening a memory-mapped view.  Used by the lightweight
    /// stats path so that 4096 prefix buckets can be queried without keeping
    /// handlers loaded.
    /// </summary>
    /// <returns>
    /// The entry count, or 0 if the file is absent, too short, or has a wrong
    /// magic number.
    /// </returns>
    public static int ReadEntryCountFromHeader(string path)
    {
        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 8,
                options: FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[8];
            var read = fs.Read(header);
            if (read < 8)
                return 0;

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[0..4]);
            if (magic != Magic)
                return 0;

            return (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        }
        catch
        {
            return 0;
        }
    }

    // -----------------------------------------------------------------------
    // Writing

    /// <summary>
    /// Writes a new sorted segment file from the supplied sorted entries.
    /// The caller <em>must</em> pass entries that are already sorted ascending
    /// by <see cref="IndexEntry.Hash"/> — this method does not sort.
    /// Uses write-then-atomic-rename for crash safety.
    /// </summary>
    public static async Task WriteAsync(
        string finalPath,
        IReadOnlyList<(Hash32 Hash, IndexEntry Entry)> sortedEntries,
        CancellationToken ct = default)
    {
        if (sortedEntries.Count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sortedEntries), "Too many entries.");

        var entryCount = sortedEntries.Count;
        var totalBytes = HeaderSize + (long)entryCount * EntrySize;

        // Allocate in one shot to allow a single WriteAsync call.
        var buf = new byte[totalBytes];

        // Header
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)entryCount);

        // Entries
        var span = buf.AsSpan(HeaderSize);
        for (var i = 0; i < entryCount; i++)
        {
            var (hash, entry) = sortedEntries[i];
            var dest = span.Slice(i * EntrySize, EntrySize);

            hash.WriteBytes(dest[..32]);
            BinaryPrimitives.WriteUInt32LittleEndian(dest[32..36], (uint)entry.FileNo);
            BinaryPrimitives.WriteUInt64LittleEndian(dest[36..44], (ulong)entry.Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(dest[44..48], (uint)entry.Length);
        }

        await FileAtomicHelper.WriteAtomicAsync(finalPath, buf, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Filename helpers

    /// <summary>
    /// Returns the compaction level (0, 1, or 2) encoded in a segment filename
    /// such as <c>chunks000.seg-012.idx</c> or the legacy <c>seg-012.idx</c>.
    /// Returns -1 if the filename does not contain the expected <c>.seg-</c> or
    /// <c>seg-</c> pattern.
    /// </summary>
    public static int GetLevel(string segmentFileName)
    {
        var name = Path.GetFileNameWithoutExtension(segmentFileName);

        // New format: "{prefix}.seg-{level}{nn}"  (e.g. "chunks000.seg-012")
        var dotSeg = name.IndexOf(".seg-", StringComparison.Ordinal);
        int levelCharIndex;
        if (dotSeg >= 0)
        {
            levelCharIndex = dotSeg + 5; // skip ".seg-"
        }
        else if (name.StartsWith("seg-", StringComparison.Ordinal))
        {
            // Legacy format: "seg-{level}{nn}"
            levelCharIndex = 4;
        }
        else
        {
            return -1;
        }

        if (levelCharIndex >= name.Length)
            return -1;

        return name[levelCharIndex] switch
        {
            '0' => 0,
            '1' => 1,
            '2' => 2,
            _   => -1
        };
    }

    /// <summary>
    /// Returns the maximum entry count permitted at the given compaction level.
    /// </summary>
    public static int MaxEntriesForLevel(int level) => level switch
    {
        0 => Level0MaxEntries,
        1 => Level1MaxEntries,
        2 => Level2MaxEntries,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    // -----------------------------------------------------------------------
    // Private helpers

    // Reusable scratch buffer for hash reads — avoids per-call allocation.
    // Only accessed under a binary search which is single-threaded per segment.
    [ThreadStatic]
    private static byte[]? _hashReadBuf;

    private void ReadHashAt(long entryOffset, Span<byte> dest)
    {
        // MemoryMappedViewAccessor.ReadArray requires a managed byte[].
        // We use a ThreadStatic 32-byte scratch buffer to avoid per-call
        // allocation on the binary-search hot path.
        var buf = _hashReadBuf ??= new byte[32];
        _view.ReadArray(entryOffset, buf, 0, 32);
        buf.AsSpan(0, 32).CopyTo(dest);
    }

    // -----------------------------------------------------------------------
    // Sequential read (for compaction merges)

    /// <summary>
    /// Reads all entries from the segment in sorted order.
    /// Used only during compaction — not on the hot read path.
    /// </summary>
    public List<(Hash32 Hash, IndexEntry Entry)> ReadAllEntries()
    {
        var result = new List<(Hash32, IndexEntry)>(EntryCount);
        var buf    = new byte[32];

        for (var i = 0; i < EntryCount; i++)
        {
            var offset = HeaderSize + (long)i * EntrySize;
            _view.ReadArray(offset, buf, 0, 32);

            var hash   = new Hash32(buf);
            var fileNo = (int) _view.ReadUInt32(offset + 32);
            var dataOff= (long)_view.ReadInt64(offset + 36);
            var length = (int) _view.ReadUInt32(offset + 44);

            result.Add((hash, new IndexEntry(fileNo, dataOff, length)));
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.Dispose();
        _mmf.Dispose();
    }
}

/// <summary>
/// A resolved index entry: the location of a chunk or file-definition blob
/// within a named pack file.
/// </summary>
internal readonly record struct IndexEntry(int FileNo, long Offset, int Length);
