namespace BinStash.Core.Types;

public readonly struct Hash8 : IEquatable<Hash8>, IComparable<Hash8>
{
    private readonly ulong _h0;

    
    public Hash8(ulong hash)
    {
        _h0 = hash;
    }
    
    public Hash8(byte[] bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(bytes.Length, 8);
        _h0 = BitConverter.ToUInt64(bytes, 0);
    }
    
    public Hash8(ReadOnlySpan<byte> bytes)
    {
        /* pack 4*8 little-endian */
        ArgumentOutOfRangeException.ThrowIfNotEqual(bytes.Length, 8);
        _h0 = BitConverter.ToUInt64(bytes);
    }
    
    public static Hash8 FromHexString(string hex)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(hex.Length, 16);
        var bytes = Convert.FromHexString(hex);
        return new Hash8(bytes);
    }
    
    public bool Equals(Hash8 other) => _h0 == other._h0;

    public int CompareTo(Hash8 other) => _h0.CompareTo(other._h0);
    
    public override int GetHashCode() => _h0.GetHashCode();

    public byte[] GetBytes()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes, _h0);
        return bytes;
    }
    
    public string ToHexString() => Convert.ToHexStringLower(GetBytes());
}