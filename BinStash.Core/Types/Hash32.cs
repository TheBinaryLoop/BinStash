namespace BinStash.Core.Types;

public readonly struct Hash32 : IEquatable<Hash32>, IComparable<Hash32>
{
    private readonly ulong _h0, _h1, _h2, _h3; // pack 32 bytes into 4x ulong

    public Hash32(byte[] bytes)
    {
        /* pack 4*8 little-endian */
        if (bytes.Length != 32) throw new ArgumentException("Hash32 must be 32 bytes");
        _h0 = BitConverter.ToUInt64(bytes, 0);
        _h1 = BitConverter.ToUInt64(bytes, 8);
        _h2 = BitConverter.ToUInt64(bytes, 16);
        _h3 = BitConverter.ToUInt64(bytes, 24);
    }
    
    public Hash32(ReadOnlySpan<byte> bytes)
    {
        /* pack 4*8 little-endian */
        if (bytes.Length != 32) throw new ArgumentException("Hash32 must be 32 bytes");
        _h0 = BitConverter.ToUInt64(bytes.Slice(0, 8).ToArray());
        _h1 = BitConverter.ToUInt64(bytes.Slice(8, 8).ToArray());
        _h2 = BitConverter.ToUInt64(bytes.Slice(16, 8).ToArray());
        _h3 = BitConverter.ToUInt64(bytes.Slice(24, 8).ToArray()); 
    }
    
    public static Hash32 FromHexString(string hex)
    {
        if (hex.Length != 64) throw new ArgumentException("Hash32 hex string must be 64 characters");
        var bytes = Convert.FromHexString(hex);
        return new Hash32(bytes);
    }
    
    public bool Equals(Hash32 other) => _h0 == other._h0 && _h1 == other._h1 && _h2 == other._h2 && _h3 == other._h3;
    public int CompareTo(Hash32 other)
    {
        var c = _h0.CompareTo(other._h0);
        if (c != 0) return c;
        c = _h1.CompareTo(other._h1);
        if (c != 0) return c;
        c = _h2.CompareTo(other._h2);
        if (c != 0) return c;
        return _h3.CompareTo(other._h3);
    }

    public override int GetHashCode() => HashCode.Combine(_h0,_h1,_h2,_h3);
    
    public byte[] GetBytes()
    {
        var bytes = new byte[32];
        Array.Copy(BitConverter.GetBytes(_h0), 0, bytes, 0, 8);
        Array.Copy(BitConverter.GetBytes(_h1), 0, bytes, 8, 8);
        Array.Copy(BitConverter.GetBytes(_h2), 0, bytes, 16, 8);
        Array.Copy(BitConverter.GetBytes(_h3), 0, bytes, 24, 8);
        return bytes;
    }
    
    public string ToHexString() => Convert.ToHexStringLower(GetBytes());
}
