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

using System.Buffers;
using System.Text;
using BinStash.Contracts.Hashing;
using BinStash.Core.Serialization.Utils;

namespace BinStash.Core.Serialization;

/// <summary>
/// Canonical TLV + UVarInt node writer + hash.
/// Designed for Merkle node blobs: stable bytes, versionable via tagged fields.
/// </summary>
public static class CanonicalNodes
{
    // ---- Wire constants -----------------------------------------------------

    private static readonly byte[] Magic = "BSTN"u8.ToArray(); // 4 bytes
    private const byte NodeFormatVersion = 0x01;

    public enum FieldType : uint
    {
        UVarInt = 1,
        Bytes   = 2,
        Utf8    = 3,
        Hash32  = 4,
        Vector  = 5,
        Map     = 6
    }

    // ---- Public API ---------------------------------------------------------

    /// <summary>
    /// Builds canonical node bytes:
    ///   Magic(4) || nodeFormatVersion(1) || nodeType(1) || TLV fields...
    /// Fields MUST be added with strictly increasing fieldId.
    /// </summary>
    public sealed class NodeBuilder
    {
        private readonly byte _nodeType;
        private readonly ArrayBufferWriter<byte> _buf = new();
        private uint _lastFieldId;

        public NodeBuilder(byte nodeType)
        {
            _nodeType = nodeType;

            _buf.Write(Magic);
            _buf.WriteByte(NodeFormatVersion);
            _buf.WriteByte(_nodeType);
        }

        public byte[] ToArray() => _buf.WrittenSpan.ToArray();

        /// <summary>Hash of the node bytes.</summary>
        public Hash32 ComputeHash32(Func<ReadOnlySpan<byte>, Hash32>? hash32 = null)
        {
            var bytes = _buf.WrittenSpan;
            hash32 ??= HashInternal;
            var h = hash32(bytes);
            return h;
        }

        // --- Field writers (TLV) --------------------------------------------

        public NodeBuilder FieldUVarInt(uint fieldId, ulong value)
        {
            Span<byte> tmp = stackalloc byte[16];
            var len = VarIntUtils.WriteVarInt(tmp, value);
            return FieldRaw(fieldId, FieldType.UVarInt, tmp[..len]);
        }

        public NodeBuilder FieldUVarInt(uint fieldId, long value)
        {
            Span<byte> tmp = stackalloc byte[16];
            var len = VarIntUtils.WriteVarInt(tmp, value);
            return FieldRaw(fieldId, FieldType.UVarInt, tmp[..len]);
        }

        public NodeBuilder FieldBytes(uint fieldId, ReadOnlySpan<byte> bytes)
            => FieldRaw(fieldId, FieldType.Bytes, bytes);

        public NodeBuilder FieldUtf8(uint fieldId, string? value)
        {
            var b = Encoding.UTF8.GetBytes(value ?? string.Empty);
            return FieldRaw(fieldId, FieldType.Utf8, b);
        }

        public NodeBuilder FieldHash32(uint fieldId, ReadOnlySpan<byte> hash32)
        {
            if (hash32.Length != 32) throw new ArgumentException("Hash32 must be exactly 32 bytes.", nameof(hash32));
            return FieldRaw(fieldId, FieldType.Hash32, hash32);
        }

        /// <summary>
        /// Vector is just a length-framed blob (field length frames it) containing concatenated items.
        /// Caller decides the per-item encoding; typically a sequence of primitives (e.g., Hash32||UVarInt||UVarInt).
        /// </summary>
        public NodeBuilder FieldVector(uint fieldId, ReadOnlySpan<byte> vectorPayload)
            => FieldRaw(fieldId, FieldType.Vector, vectorPayload);

        /// <summary>
        /// Canonical map: entries are sorted by raw UTF-8 bytes of the key.
        /// Encoding: UVarInt(count) || for each entry: Bytes(keyUtf8) || valueBytes
        /// valueBytes should be fixed-size (e.g., Hash32) or self-framed.
        /// </summary>
        public NodeBuilder FieldMapUtf8ToHash32(uint fieldId, IEnumerable<(string Key, byte[] Hash32)> entries)
        {
            var list = entries
                .Select(e => (KeyBytes: Encoding.UTF8.GetBytes(e.Key ?? string.Empty), e.Hash32))
                .ToList();

            foreach (var (_, h) in list)
                if (h is null || h.Length != 32)
                    throw new ArgumentException("All map values must be 32-byte hashes.");

            list.Sort((a, b) => LexCompare(a.KeyBytes, b.KeyBytes));

            var payload = new ArrayBufferWriter<byte>();
            VarIntUtils.WriteVarInt(payload, (ulong)list.Count);

            foreach (var (k, h) in list)
            {
                WriteBytes(payload, k);   // Bytes(key)
                payload.Write(h);         // value = 32 bytes
            }

            return FieldRaw(fieldId, FieldType.Map, payload.WrittenSpan);
        }

        /// <summary>
        /// Canonical map: entries sorted by key UTF-8 bytes. Values are UTF-8 bytes.
        /// Encoding: UVarInt(count) || for each: Bytes(key) || Bytes(value)
        /// </summary>
        public NodeBuilder FieldMapUtf8ToUtf8(uint fieldId, IEnumerable<(string Key, string Value)> entries)
        {
            var list = entries
                .Select(e => (
                    KeyBytes: Encoding.UTF8.GetBytes(e.Key ?? string.Empty),
                    ValBytes: Encoding.UTF8.GetBytes(e.Value ?? string.Empty)))
                .ToList();

            list.Sort((a, b) => LexCompare(a.KeyBytes, b.KeyBytes));

            var payload = new ArrayBufferWriter<byte>();
            VarIntUtils.WriteVarInt(payload, (ulong)list.Count);

            foreach (var (k, v) in list)
            {
                WriteBytes(payload, k);
                WriteBytes(payload, v);
            }

            return FieldRaw(fieldId, FieldType.Map, payload.WrittenSpan);
        }

        // --- Core TLV ---------------------------------------------------------

        private NodeBuilder FieldRaw(uint fieldId, FieldType typeId, ReadOnlySpan<byte> valueBytes)
        {
            if (fieldId == 0) throw new ArgumentOutOfRangeException(nameof(fieldId), "fieldId must be >= 1.");
            if (fieldId <= _lastFieldId)
                throw new InvalidOperationException($"Fields must be added in strictly increasing order. Last={_lastFieldId}, new={fieldId}.");

            _lastFieldId = fieldId;

            // TLV: fieldId(varint) || typeId(varint) || len(varint) || valueBytes
            VarIntUtils.WriteVarInt(_buf, fieldId);
            VarIntUtils.WriteVarInt(_buf, (uint)typeId);
            VarIntUtils.WriteVarInt(_buf, (ulong)valueBytes.Length);
            _buf.Write(valueBytes);
            return this;
        }
    }

    // ---- Helpers: UVarInt, Bytes, Lex compare, hashing ----------------------
    
    public static void WriteBytes(IBufferWriter<byte> w, ReadOnlySpan<byte> bytes)
    {
        VarIntUtils.WriteVarInt(w, (ulong)bytes.Length);
        w.Write(bytes);
    }

    private static int LexCompare(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var diff = a[i] - b[i];
            if (diff != 0) return diff;
        }
        return a.Length - b.Length;
    }

    private static Hash32 HashInternal(ReadOnlySpan<byte> data)
    {
        return new Hash32(Blake3.Hasher.Hash(data).AsSpan());
    }

    // Small IBufferWriter helpers
    private static void Write(this IBufferWriter<byte> w, ReadOnlySpan<byte> src)
    {
        var span = w.GetSpan(src.Length);
        src.CopyTo(span);
        w.Advance(src.Length);
    }

    private static void Write(this IBufferWriter<byte> w, byte[] src) => w.Write((ReadOnlySpan<byte>)src);

    private static void WriteByte(this IBufferWriter<byte> w, byte value)
    {
        var span = w.GetSpan(1);
        span[0] = value;
        w.Advance(1);
    }
}