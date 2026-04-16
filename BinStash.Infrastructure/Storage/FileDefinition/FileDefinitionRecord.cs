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

namespace BinStash.Infrastructure.Storage.FileDefinition;

/// <summary>
/// Self-describing binary record stored inside a BSPK pack entry for each
/// unique file definition in the object store.
///
/// <para>
/// <strong>Wire format (all fixed fields are little-endian):</strong>
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
/// The pack-entry index key is <c>BLAKE3(entire record blob)</c>, making the
/// file-definition category fully self-keyed — identical to how chunk entries
/// work.  <see cref="IndexedPackFileHandler.RebuildIndexFile"/> therefore
/// needs no special treatment for file definitions.
/// </para>
///
/// <para>
/// To look up a file definition by its <em>file content hash</em> the caller
/// must know the <see cref="StorageKey"/> (= <c>BLAKE3(record blob)</c>).
/// This is persisted as <c>FileDefinition.StorageKey</c> in the database when
/// the record is first written.
/// </para>
/// </summary>
public sealed class FileDefinitionRecord
{
    // -----------------------------------------------------------------------
    // Format constants

    public  const uint   Magic         = 0x44465342; // "BSFD" LE
    public  const byte   CurrentVersion = 1;
    private const int    FixedHeaderSize = 4 + 1 + 32 + 8 + 4; // 49 bytes

    // -----------------------------------------------------------------------
    // Fields

    /// <summary>BLAKE3 hash of the original file content (the "file hash" used by callers).</summary>
    public required Hash32 FileHash { get; init; }

    /// <summary>Size of the original file in bytes.</summary>
    public required long FileLength { get; init; }

    /// <summary>Ordered list of BLAKE3 chunk hashes that reconstruct the file.</summary>
    public required IReadOnlyList<Hash32> ChunkHashes { get; init; }

    // -----------------------------------------------------------------------
    // Serialisation

    /// <summary>
    /// Serialises this record to a byte array ready to be passed to
    /// <see cref="ObjectStore.WriteFileDefinitionAsync"/>.
    /// </summary>
    public byte[] Serialize()
    {
        var compressedChunks = ChecksumCompressor.TransposeCompress(
            ChunkHashes.Select(static h => h.GetBytes()).ToList());

        var totalSize = FixedHeaderSize + compressedChunks.Length;
        var buf = GC.AllocateUninitializedArray<byte>(totalSize);

        var span = buf.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4],   Magic);
        span[4] = CurrentVersion;
        FileHash.WriteBytes(span[5..37]);
        BinaryPrimitives.WriteInt64LittleEndian(span[37..45], FileLength);
        BinaryPrimitives.WriteInt32LittleEndian(span[45..49], ChunkHashes.Count);
        compressedChunks.CopyTo(span[49..]);

        return buf;
    }

    /// <summary>
    /// Computes <c>BLAKE3(Serialize())</c> — the pack-store index key for
    /// this record.  Must be persisted as <c>FileDefinition.StorageKey</c>.
    /// </summary>
    public Hash32 ComputeStorageKey()
    {
        var blob = Serialize();
        return new Hash32(Hasher.Hash(blob).AsSpan());
    }

    // -----------------------------------------------------------------------
    // Deserialisation

    /// <summary>
    /// Deserialises a <see cref="FileDefinitionRecord"/> from the raw bytes
    /// returned by <see cref="ObjectStore.ReadFileDefinitionBlobAsync"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">Blob is too short, magic mismatch, or unsupported version.</exception>
    public static FileDefinitionRecord Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedHeaderSize)
            throw new InvalidDataException($"FileDefinitionRecord blob is too short ({data.Length} bytes).");

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (magic != Magic)
            throw new InvalidDataException($"FileDefinitionRecord magic mismatch: expected 0x{Magic:X8}, got 0x{magic:X8}.");

        var version = data[4];
        if (version != CurrentVersion)
            throw new NotSupportedException($"Unsupported FileDefinitionRecord version {version}.");

        var fileHash   = new Hash32(data[5..37]);
        var fileLength = BinaryPrimitives.ReadInt64LittleEndian(data[37..45]);
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(data[45..49]);

        var compressedChunks = data[49..].ToArray();
        var chunkHashes = ChecksumCompressor.TransposeDecompressHashes(compressedChunks);

        if (chunkHashes.Count != chunkCount)
            throw new InvalidDataException(
                $"FileDefinitionRecord chunk count mismatch: header says {chunkCount}, decoded {chunkHashes.Count}.");

        return new FileDefinitionRecord
        {
            FileHash    = fileHash,
            FileLength  = fileLength,
            ChunkHashes = chunkHashes
        };
    }

    /// <summary>
    /// Async variant of <see cref="Deserialize(ReadOnlySpan{byte})"/> that
    /// reads from a stream positioned at the start of the record.
    /// </summary>
    public static async Task<FileDefinitionRecord> DeserializeAsync(
        Stream stream, CancellationToken ct = default)
    {
        var buf = new byte[FixedHeaderSize];
        await ReadExactlyAsync(stream, buf, ct).ConfigureAwait(false);
        var span = buf.AsSpan();

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(span[..4]);
        if (magic != Magic)
            throw new InvalidDataException($"FileDefinitionRecord magic mismatch: expected 0x{Magic:X8}, got 0x{magic:X8}.");

        var version = span[4];
        if (version != CurrentVersion)
            throw new NotSupportedException($"Unsupported FileDefinitionRecord version {version}.");

        var fileHash   = new Hash32(span[5..37]);
        var fileLength = BinaryPrimitives.ReadInt64LittleEndian(span[37..45]);
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(span[45..49]);

        // Read the rest (compressed chunk hashes) into a MemoryStream and parse
        var remainder = new MemoryStream();
        await stream.CopyToAsync(remainder, ct).ConfigureAwait(false);
        remainder.Position = 0;

        var chunkHashes = ChecksumCompressor.TransposeDecompressHashes(remainder);

        if (chunkHashes.Count != chunkCount)
            throw new InvalidDataException(
                $"FileDefinitionRecord chunk count mismatch: header says {chunkCount}, decoded {chunkHashes.Count}.");

        return new FileDefinitionRecord
        {
            FileHash    = fileHash,
            FileLength  = fileLength,
            ChunkHashes = chunkHashes
        };
    }

    // -----------------------------------------------------------------------
    // Helpers

    private static async Task ReadExactlyAsync(Stream stream, byte[] buf, CancellationToken ct)
    {
        var total = 0;
        while (total < buf.Length)
        {
            var read = await stream.ReadAsync(buf.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while reading FileDefinitionRecord header.");
            total += read;
        }
    }
}
