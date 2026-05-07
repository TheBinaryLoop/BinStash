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

using BinStash.Core.Serialization.Utils;
using FluentAssertions;
using FsCheck.Xunit;

namespace BinStash.Core.Tests;

public class VarIntSpecs
{
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_ushort(ushort value)
    {
        RoundTrip(value).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_int(int value)
    {
        RoundTrip(value).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_uint(uint value)
    {
        RoundTrip(value).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_long(long value)
    {
        RoundTrip(value).Should().Be(value);
    }

    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_ulong(ulong value)
    {
        RoundTrip(value).Should().Be(value);
    }
    
    [Property]
    public void Zero_is_encoded_minimally()
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, 0);
        ms.Length.Should().Be(1);
    }
    
    [Theory]
    [InlineData((ulong)0, 1)]
    [InlineData((ulong)1, 1)]
    [InlineData((ulong)127, 1)]
    [InlineData((ulong)128, 2)]
    [InlineData((ulong)255, 2)]
    [InlineData((ulong)16383, 2)]
    [InlineData((ulong)16384, 3)]
    [InlineData((ulong)2097151, 3)]
    [InlineData((ulong)2097152, 4)]
    public void Unsigned_values_have_expected_encoded_length(ulong value, int expectedLength)
    {
        using var ms = new MemoryStream();

        VarIntUtils.WriteVarInt(ms, value);

        ms.Length.Should().Be(expectedLength);
    }
    
    [Fact]
    public void UShort_min_roundtrips()
    {
        RoundTrip(ushort.MinValue).Should().Be(ushort.MinValue);
    }

    [Fact]
    public void UShort_max_roundtrips()
    {
        RoundTrip(ushort.MaxValue).Should().Be(ushort.MaxValue);
    }
    
    [Fact]
    public void Int_min_roundtrips()
    {
        RoundTrip(int.MinValue).Should().Be(int.MinValue);
    }

    [Fact]
    public void Int_max_roundtrips()
    {
        RoundTrip(int.MaxValue).Should().Be(int.MaxValue);
    }
    
    [Fact]
    public void UInt_min_roundtrips()
    {
        RoundTrip(uint.MinValue).Should().Be(uint.MinValue);
    }

    [Fact]
    public void UInt_max_roundtrips()
    {
        RoundTrip(uint.MaxValue).Should().Be(uint.MaxValue);
    }
    
    [Fact]
    public void Long_min_roundtrips()
    {
        RoundTrip(long.MinValue).Should().Be(long.MinValue);
    }

    [Fact]
    public void Long_max_roundtrips()
    {
        RoundTrip(long.MaxValue).Should().Be(long.MaxValue);
    }
    
    [Fact]
    public void ULong_min_roundtrips()
    {
        RoundTrip(ulong.MinValue).Should().Be(ulong.MinValue);
    }

    [Fact]
    public void ULong_max_roundtrips()
    {
        RoundTrip(ulong.MaxValue).Should().Be(ulong.MaxValue);
    }
    
    [Theory]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x80 })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF })]
    public void ReadVarInt_throws_on_truncated_input(byte[] buffer)
    {
        using var ms = new MemoryStream(buffer);
        using var reader = new BinaryReader(ms);

        var act = () => VarIntUtils.ReadVarInt<uint>(reader);

        act.Should().Throw<EndOfStreamException>();
    }
    
    [Fact]
    public void ReadVarInt_uint_throws_on_too_many_bytes()
    {
        using var ms = new MemoryStream([0x80, 0x80, 0x80, 0x80, 0x80, 0x01]);
        using var reader = new BinaryReader(ms);

        var act = () => VarIntUtils.ReadVarInt<uint>(reader);

        act.Should().Throw<FormatException>();
    }
    
    [Fact]
    public void Non_minimal_zero_decodes_and_reencodes_minimally()
    {
        using var ms = new MemoryStream([0x80, 0x00]);
        using var reader = new BinaryReader(ms);

        var value = VarIntUtils.ReadVarInt<uint>(reader);
        value.Should().Be(0u);

        using var ms2 = new MemoryStream();
        VarIntUtils.WriteVarInt(ms2, value);

        ms2.ToArray().Should().Equal(new byte[] { 0x00 });
    }
    
    [Theory]
    [InlineData((ulong)0,    new byte[] { 0x00 })]
    [InlineData((ulong)1,    new byte[] { 0x01 })]
    [InlineData((ulong)127,  new byte[] { 0x7F })]
    [InlineData((ulong)128,  new byte[] { 0x80, 0x01 })]
    [InlineData((ulong)255,  new byte[] { 0xFF, 0x01 })]
    [InlineData((ulong)300,  new byte[] { 0xAC, 0x02 })]
    public void Unsigned_values_encode_to_expected_bytes(ulong value, byte[] expected)
    {
        using var ms = new MemoryStream();

        VarIntUtils.WriteVarInt(ms, value);

        ms.ToArray().Should().Equal(expected);
    }
    
    [Theory]
    [InlineData(0,  new byte[] { 0x00 })]
    [InlineData(-1, new byte[] { 0x01 })]
    [InlineData(1,  new byte[] { 0x02 })]
    [InlineData(-2, new byte[] { 0x03 })]
    [InlineData(2,  new byte[] { 0x04 })]
    public void Signed_int_values_encode_to_expected_bytes(int value, byte[] expected)
    {
        using var ms = new MemoryStream();

        VarIntUtils.WriteVarInt(ms, value);

        ms.ToArray().Should().Equal(expected);
    }
    
    [Fact]
    public void ReadVarInt_consumes_only_its_own_bytes()
    {
        using var ms = new MemoryStream([0xAC, 0x02, 0x7F]);
        using var reader = new BinaryReader(ms);

        var value = VarIntUtils.ReadVarInt<uint>(reader);
        value.Should().Be(300);

        reader.ReadByte().Should().Be(0x7F);
    }
    
    [Fact]
    public void UInt_encoding_of_small_value_can_be_read_as_ulong()
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, (uint)300);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        VarIntUtils.ReadVarInt<ulong>(reader).Should().Be(300);
    }
    
    [Fact]
    public void Reading_large_ulong_as_uint_throws()
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, (ulong)uint.MaxValue + 1);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var act = () => VarIntUtils.ReadVarInt<uint>(reader);

        act.Should().Throw<FormatException>();
    }
    
    [Property(MaxTest = 5000)]
    public void Unsigned_encoding_is_minimal(uint value)
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);

        var bytes = ms.ToArray();

        if (bytes.Length > 1)
        {
            bytes[^1].Should().BeLessThan(0x80);
            bytes[^1].Should().NotBe(0);
        }
    }
    
    private static T RoundTrip<T>(T value) where T : unmanaged
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        return VarIntUtils.ReadVarInt<T>(reader);
    }

    private static byte[] Encode<T>(T value) where T : unmanaged
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        return ms.ToArray();
    }
}
