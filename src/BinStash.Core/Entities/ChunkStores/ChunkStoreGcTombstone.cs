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

using BinStash.Contracts.Hashing;

namespace BinStash.Core.Entities;

/// <summary>
/// Which pack-store category a garbage-collected object lives in.
/// </summary>
public enum GcObjectCategory : byte
{
    Chunk = 0,
    FileDefinition = 1
}

/// <summary>
/// A quarantined object: one chunk or file definition that the mark phase of a
/// garbage-collection run found unreachable, but that has <em>not</em> been
/// physically removed yet.
///
/// <para>
/// A tombstone is the pivot of the online GC protocol. Creating one is done in
/// the same transaction that deletes the object's <see cref="Chunk"/> /
/// <see cref="FileDefinition"/> row, which makes the object invisible to the
/// deduplication ("which of these do you already have?") path while leaving the
/// bytes physically present and readable. That combination gives two properties
/// that together make GC safe to run against live traffic:
/// </para>
/// <list type="number">
///   <item>
///     Any ingest that <em>starts</em> after quarantine is told the object is
///     missing and simply re-uploads it. No dangling reference is possible.
///   </item>
///   <item>
///     Any ingest that was told "present" <em>before</em> quarantine is repaired at
///     finalize time: the referenced tombstone is deleted and the catalogue row
///     restored from the fields recorded here (<em>resurrection</em>). Because the
///     bytes were never removed, resurrection cannot fail.
///   </item>
/// </list>
/// <para>
/// Only once <see cref="EligibleAt"/> has passed may the reclaim phase physically
/// drop the bytes, at which point the tombstone is deleted too. Until then the
/// quarantine is completely reversible.
/// </para>
/// </summary>
public class ChunkStoreGcTombstone
{
    /// <summary>The chunk store the object belongs to. Part of the primary key.</summary>
    public Guid ChunkStoreId { get; set; }

    /// <summary>Whether this is a chunk or a file definition. Part of the primary key.</summary>
    public GcObjectCategory Category { get; set; }

    /// <summary>
    /// BLAKE3 content hash of the object — the chunk checksum, or the file hash of a
    /// file definition. Part of the primary key.
    /// </summary>
    public required Hash32 Checksum { get; set; }

    /// <summary>
    /// The three-hex-character pack-store bucket the object lives in — the first characters of
    /// <see cref="Checksum"/>, stored explicitly rather than derived.
    ///
    /// <para>
    /// The reclaim phase works one bucket at a time, because that is the granularity at which
    /// pack files are rewritten. Recovering the bucket from the hash would mean either scanning
    /// every tombstone in the store or asking the database to slice a <c>bytea</c>; a plain
    /// indexed column makes it a range scan.
    /// </para>
    /// </summary>
    public required string BucketPrefix { get; set; }

    /// <summary>The garbage-collection run that quarantined this object.</summary>
    public Guid RunId { get; set; }

    /// <summary>When the object was quarantined.</summary>
    public DateTimeOffset QuarantinedAt { get; set; }

    /// <summary>
    /// The earliest instant at which the reclaim phase may physically drop the bytes.
    /// Set to <see cref="QuarantinedAt"/> plus the run's retention window.
    /// </summary>
    public DateTimeOffset EligibleAt { get; set; }

    /// <summary>Pack file number the object was located in when it was quarantined.</summary>
    public int PackFileNo { get; set; }

    /// <summary>Byte offset of the pack entry within the pack file.</summary>
    public long PackOffset { get; set; }

    /// <summary>Physical size of the pack entry (header plus compressed payload).</summary>
    public int PackLength { get; set; }

    /// <summary>
    /// Uncompressed size: <see cref="Chunk.Length"/> for a chunk, <see cref="FileDefinition.Length"/>
    /// for a file definition. Recorded so the catalogue row can be restored on resurrection.
    /// </summary>
    public long LogicalLength { get; set; }

    /// <summary>
    /// Compressed size, used to restore <see cref="Chunk.CompressedLength"/> on resurrection.
    /// Always 0 for file definitions.
    /// </summary>
    public int CompressedLength { get; set; }
}
