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
using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;

namespace BinStash.Infrastructure.Storage.S3;

/// <summary>
/// The location of a single chunk within a pack object stored in S3.
/// </summary>
/// <param name="PackKey">S3 object key of the pack file (includes prefix).</param>
/// <param name="Offset">Byte offset of the pack entry within the pack object.</param>
/// <param name="Length">Total byte length of the pack entry (header + compressed payload).</param>
internal readonly record struct ChunkLocation(string PackKey, long Offset, int Length);

/// <summary>
/// In-memory index that maps BLAKE3 chunk hashes (hex) to their location in S3 pack objects.
/// Persisted to S3 as a compact binary file (<c>{prefix}chunks/index.bin</c>).
/// </summary>
/// <remarks>
/// Binary format:
/// <list type="bullet">
///   <item>Magic: 4 bytes (<c>S3IX</c>)</item>
///   <item>Version: 1 byte</item>
///   <item>Entry count: 4 bytes (int32, little-endian)</item>
///   <item>Per entry: 32-byte hash + 2-byte pack-key length + N-byte pack key + 8-byte offset + 4-byte length</item>
/// </list>
/// </remarks>
internal sealed class S3PackIndex
{
    private const uint Magic = 0x58493353; // "S3IX" stored as little-endian uint32
    private const byte CurrentVersion = 1;

    private readonly ConcurrentDictionary<string, ChunkLocation> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of chunk entries currently in the index.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Attempts to look up a chunk by its hex-encoded BLAKE3 hash.
    /// Thread-safe; can be called concurrently with <see cref="Add"/>.
    /// </summary>
    public bool TryGet(string hexHash, out ChunkLocation location)
        => _entries.TryGetValue(hexHash, out location);

    /// <summary>
    /// Adds or updates the location for a chunk hash.
    /// Thread-safe.
    /// </summary>
    public void Add(string hexHash, ChunkLocation location)
        => _entries[hexHash] = location;

    /// <summary>
    /// Loads the index from S3. If the object does not exist (new store), the index remains empty.
    /// </summary>
    public async Task LoadFromS3Async(IAmazonS3 client, string bucket, string indexKey, CancellationToken ct = default)
    {
        try
        {
            using var response = await client.GetObjectAsync(bucket, indexKey, ct).ConfigureAwait(false);
            await using var stream = response.ResponseStream;
            Deserialize(stream);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            // New store — start with an empty index. Not an error.
        }
    }

    /// <summary>
    /// Serializes the index and uploads it to S3.
    /// </summary>
    /// <returns>The number of bytes written to S3.</returns>
    public async Task<long> SaveToS3Async(IAmazonS3 client, string bucket, string indexKey, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        Serialize(ms);
        var indexSize = ms.Length;
        ms.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = indexKey,
            InputStream = ms,
        };

        await client.PutObjectAsync(request, ct).ConfigureAwait(false);
        return indexSize;
    }

    private void Serialize(Stream output)
    {
        Span<byte> buf = stackalloc byte[8];
        Span<byte> hashBytes = stackalloc byte[32];

        // Magic
        BinaryPrimitives.WriteUInt32LittleEndian(buf[..4], Magic);
        output.Write(buf[..4]);

        // Version
        output.WriteByte(CurrentVersion);

        // Entry count
        var snapshot = _entries.ToArray(); // stable snapshot for serialization
        BinaryPrimitives.WriteInt32LittleEndian(buf[..4], snapshot.Length);
        output.Write(buf[..4]);

        foreach (var (hexHash, loc) in snapshot)
        {
            // Hash: 32 bytes binary
            Convert.FromHexString(hexHash, hashBytes, out _, out _);
            output.Write(hashBytes);

            // PackKey: 2-byte length prefix + UTF-8 bytes
            var packKeyBytes = System.Text.Encoding.UTF8.GetBytes(loc.PackKey);
            BinaryPrimitives.WriteUInt16LittleEndian(buf[..2], (ushort)packKeyBytes.Length);
            output.Write(buf[..2]);
            output.Write(packKeyBytes);

            // Offset: 8 bytes
            BinaryPrimitives.WriteInt64LittleEndian(buf[..8], loc.Offset);
            output.Write(buf[..8]);

            // Length: 4 bytes
            BinaryPrimitives.WriteInt32LittleEndian(buf[..4], loc.Length);
            output.Write(buf[..4]);
        }
    }

    private void Deserialize(Stream input)
    {
        Span<byte> buf = stackalloc byte[8];

        // Magic
        input.ReadExactly(buf[..4]);
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(buf[..4]);
        if (magic != Magic)
            throw new InvalidDataException($"Invalid S3 index magic: expected 0x{Magic:X8}, got 0x{magic:X8}.");

        // Version
        var version = (byte)input.ReadByte();
        if (version != CurrentVersion)
            throw new NotSupportedException($"Unsupported S3 index version {version}. Expected {CurrentVersion}.");

        // Entry count
        input.ReadExactly(buf[..4]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(buf[..4]);

        for (var i = 0; i < count; i++)
        {
            // Hash: 32 bytes binary → 64-char lowercase hex
            var hashBytes = new byte[32];
            input.ReadExactly(hashBytes);
            var hexHash = Convert.ToHexStringLower(hashBytes);

            // PackKey
            input.ReadExactly(buf[..2]);
            var packKeyLen = BinaryPrimitives.ReadUInt16LittleEndian(buf[..2]);
            var packKeyBytes = new byte[packKeyLen];
            input.ReadExactly(packKeyBytes);
            var packKey = System.Text.Encoding.UTF8.GetString(packKeyBytes);

            // Offset
            input.ReadExactly(buf[..8]);
            var offset = BinaryPrimitives.ReadInt64LittleEndian(buf[..8]);

            // Length
            input.ReadExactly(buf[..4]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(buf[..4]);

            _entries[hexHash] = new ChunkLocation(packKey, offset, length);
        }
    }
}
