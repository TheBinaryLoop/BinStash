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
using BinStash.Core.Serialization.Utils;

namespace BinStash.StoreMigration;

/// <summary>
/// Reads the old flat varint append-log index files (<c>index{prefix}.idx</c>)
/// that pre-date the BINST-99 LSM-tree rewrite.
///
/// <para>
/// <strong>Old flat index record format (per entry):</strong>
/// <code>
///   32 bytes  Hash    (raw BLAKE3)
///   varint    FileNo  (signed int32, zigzag)
///   varint    Offset  (signed int64, zigzag)
///   varint    Length  (signed int32, zigzag)
/// </code>
/// Records are written sequentially with no record separator; EOF terminates.
/// The file is an append log — duplicate hashes may appear (last write wins, but
/// in practice each hash appears exactly once in the FileDef store).
/// </para>
/// </summary>
internal static class OldFlatIndexReader
{
    /// <summary>
    /// Reads all index entries from a single old flat index file.
    /// Duplicate hashes are resolved by keeping the last entry seen.
    /// </summary>
    /// <param name="path">Full path to an <c>index{prefix}.idx</c> file.</param>
    /// <returns>
    /// Dictionary keyed by file hash, value = (FileNo, Offset, Length).
    /// </returns>
    public static Dictionary<Hash32, (int FileNo, long Offset, int Length)> ReadAll(string path)
    {
        var result = new Dictionary<Hash32, (int, long, int)>();

        if (!File.Exists(path))
            return result;

        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            options: FileOptions.SequentialScan);

        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        while (fs.Position < fs.Length)
        {
            // 32-byte raw hash
            var hashBytes = reader.ReadBytes(32);
            if (hashBytes.Length == 0)
                break; // EOF
            if (hashBytes.Length != 32)
                throw new InvalidDataException(
                    $"Unexpected EOF reading hash in {path} at offset {fs.Position - hashBytes.Length}.");

            var hash = new Hash32(hashBytes);

            // Signed varints (zigzag encoded)
            var fileNo = VarIntUtils.ReadVarInt<int>(reader);
            var offset = VarIntUtils.ReadVarInt<long>(reader);
            var length = VarIntUtils.ReadVarInt<int>(reader);

            result[hash] = (fileNo, offset, length);
        }

        return result;
    }
}
