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

namespace BinStash.Core.Entities;

/// <summary>
/// The chunking algorithm used by a <see cref="ChunkStore"/>.
/// </summary>
public enum ChunkerType
{
    /// <summary>
    /// FastCDC (Fast Content-Defined Chunking) — a gear-hash-based rolling-hash algorithm
    /// that splits data at content-defined boundaries for high deduplication.
    /// </summary>
    FastCdc
}

/// <summary>
/// Configuration for the chunking algorithm used by a <see cref="ChunkStore"/>.
/// Stored as an EF Core owned entity with columns prefixed by <c>Chunker_</c>.
/// <para>
/// Generic properties (applicable to all chunker types):
/// <see cref="Type"/>, <see cref="MinChunkSize"/>, <see cref="AvgChunkSize"/>, <see cref="MaxChunkSize"/>.
/// </para>
/// <para>
/// FastCDC-specific properties:
/// <see cref="ShiftCount"/>, <see cref="BoundaryCheckBytes"/>.
/// </para>
/// </summary>
public class ChunkerOptions
{
    /// <summary>
    /// The chunking algorithm to use.
    /// </summary>
    public ChunkerType Type { get; init; }

    // ── Generic chunker properties ──────────────────────────────────────

    /// <summary>
    /// Minimum chunk size in bytes. Chunks will never be smaller than this.
    /// </summary>
    public int? MinChunkSize { get; init; }

    /// <summary>
    /// Target average chunk size in bytes. The algorithm aims for this size on typical data.
    /// </summary>
    public int? AvgChunkSize { get; init; }

    /// <summary>
    /// Maximum chunk size in bytes. Chunks will never be larger than this.
    /// </summary>
    public int? MaxChunkSize { get; init; }

    // ── FastCDC-specific properties ─────────────────────────────────────

    /// <summary>
    /// FastCDC gear-hash shift count. Controls sensitivity of boundary detection.
    /// Only applicable when <see cref="Type"/> is <see cref="ChunkerType.FastCdc"/>.
    /// </summary>
    public int? ShiftCount { get; init; }

    /// <summary>
    /// Number of bytes used for boundary checking in FastCDC.
    /// Only applicable when <see cref="Type"/> is <see cref="ChunkerType.FastCdc"/>.
    /// </summary>
    public int? BoundaryCheckBytes { get; init; }

    /// <summary>
    /// Returns the default <see cref="ChunkerOptions"/> for the given <paramref name="type"/>.
    /// </summary>
    public static ChunkerOptions Default(ChunkerType type) => type switch
    {
        ChunkerType.FastCdc => new ChunkerOptions
        {
            Type = ChunkerType.FastCdc,
            MinChunkSize = 2048,
            AvgChunkSize = 65536,
            MaxChunkSize = 524288
        },
        _ => new ChunkerOptions { Type = type }
    };

    /// <summary>
    /// Validates that the chunker options are consistent.
    /// Returns a list of validation error messages (empty if valid).
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MinChunkSize is <= 0)
            errors.Add("MinChunkSize must be greater than zero.");
        if (AvgChunkSize is <= 0)
            errors.Add("AvgChunkSize must be greater than zero.");
        if (MaxChunkSize is <= 0)
            errors.Add("MaxChunkSize must be greater than zero.");

        if (MinChunkSize.HasValue && AvgChunkSize.HasValue && MinChunkSize > AvgChunkSize)
            errors.Add("MinChunkSize must not exceed AvgChunkSize.");
        if (AvgChunkSize.HasValue && MaxChunkSize.HasValue && AvgChunkSize > MaxChunkSize)
            errors.Add("AvgChunkSize must not exceed MaxChunkSize.");

        // FastCDC-specific validation
        if (Type == ChunkerType.FastCdc)
        {
            if (ShiftCount is <= 0)
                errors.Add("ShiftCount must be greater than zero when specified for FastCDC.");
            if (BoundaryCheckBytes is <= 0)
                errors.Add("BoundaryCheckBytes must be greater than zero when specified for FastCDC.");
        }

        return errors;
    }
}
