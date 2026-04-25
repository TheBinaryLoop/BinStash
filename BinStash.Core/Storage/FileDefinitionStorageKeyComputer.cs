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

using System.Buffers.Binary;
using BinStash.Contracts.Hashing;
using BinStash.Core.Compression;
using Blake3;

namespace BinStash.Core.Storage;

/// <summary>
/// Computes the <c>StorageKey</c> for a file definition record without taking a
/// dependency on <c>BinStash.Infrastructure</c>.
///
/// <para>
/// The <c>StorageKey</c> is defined as <c>BLAKE3(FileDefinitionRecord.Serialize())</c>
/// where the serialised record has the following wire format (all fixed fields
/// are little-endian):
/// <code>
///  Offset  Size  Field
///  ------  ----  -----
///       0     4  Magic          = 0x44465342  ("BSFD")
///       4     1  Version        = 1
///       5    32  FileHash       BLAKE3 of the original file content
///      37     8  FileLength     Original file size in bytes (int64)
///      45     4  ChunkCount     Number of chunks (int32)
///      49     ?  ChunkHashes    TransposeCompressed BLAKE3 chunk hash array
/// </code>
/// </para>
///
/// <para>
/// This mirrors <c>FileDefinitionRecord.ComputeStorageKey()</c> in
/// <c>BinStash.Infrastructure</c> exactly.  Any change to the Infrastructure
/// implementation must be reflected here.
/// </para>
/// </summary>
public static class FileDefinitionStorageKeyComputer
{
    private const uint Magic          = 0x44465342; // "BSFD" LE
    private const byte RecordVersion  = 1;
    private const int  FixedHeaderSize = 4 + 1 + 32 + 8 + 4; // 49 bytes

    /// <summary>
    /// Serialises a file definition record and returns
    /// <c>BLAKE3(serialised blob)</c> as the <see cref="Hash32"/> storage key.
    /// </summary>
    /// <param name="contentHash">BLAKE3 hash of the original file content.</param>
    /// <param name="fileLength">Size of the original file in bytes.</param>
    /// <param name="chunkHashes">Ordered list of BLAKE3 chunk hashes that reconstruct the file.</param>
    public static Hash32 Compute(Hash32 contentHash, long fileLength, IReadOnlyList<Hash32> chunkHashes)
    {
        var compressedChunks = ChecksumCompressor.TransposeCompress(
            chunkHashes.Select(static h => h.GetBytes()).ToList());

        var totalSize = FixedHeaderSize + compressedChunks.Length;
        var buf = GC.AllocateUninitializedArray<byte>(totalSize);
        var span = buf.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[..4],   Magic);
        span[4] = RecordVersion;
        contentHash.WriteBytes(span[5..37]);
        BinaryPrimitives.WriteInt64LittleEndian(span[37..45], fileLength);
        BinaryPrimitives.WriteInt32LittleEndian(span[45..49], chunkHashes.Count);
        compressedChunks.CopyTo(span[49..]);

        return new Hash32(Hasher.Hash(buf).AsSpan());
    }
}
