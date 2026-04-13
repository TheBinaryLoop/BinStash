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
using FluentAssertions;
using FsCheck.Xunit;

namespace BinStash.Core.Tests;

public class Hash8Specs
{
    // ---- Construction guards ------------------------------------------------

    [Fact]
    public void ByteArray_constructor_throws_for_wrong_length()
    {
        var act = () => new Hash8(new byte[7]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ByteArray_constructor_throws_for_9_bytes()
    {
        var act = () => new Hash8(new byte[9]);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ByteArray_constructor_accepts_exactly_8_bytes()
    {
        var hash = new Hash8(new byte[8]);
        hash.Should().BeOfType<Hash8>();
    }

    [Fact]
    public void Span_constructor_throws_for_wrong_length()
    {
        var bytes = new byte[4];
        // Cannot capture ReadOnlySpan<byte> in a lambda; use a local helper
        FluentAssertions.FluentActions
            .Invoking(() => new Hash8(new ReadOnlySpan<byte>(bytes)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Ulong_constructor_accepts_any_value()
    {
        var hash = new Hash8(ulong.MaxValue);
        hash.Should().BeOfType<Hash8>();
    }

    // ---- FromHexString -------------------------------------------------------

    [Fact]
    public void FromHexString_throws_for_wrong_length()
    {
        var act = () => Hash8.FromHexString("abc");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromHexString_accepts_16_char_hex()
    {
        var hex = new string('0', 16);
        var hash = Hash8.FromHexString(hex);
        hash.ToHexString().Should().Be(hex);
    }

    // ---- Round-trip -----------------------------------------------------------

    [Fact]
    public void ToHexString_FromHexString_roundtrip()
    {
        var bytes = TestUtils.RandomBytes(8, seed: 99);
        var hash = new Hash8(bytes);
        var hex = hash.ToHexString();
        Hash8.FromHexString(hex).Should().Be(hash);
    }

    [Fact]
    public void GetBytes_roundtrips_to_equal_hash()
    {
        var bytes = TestUtils.RandomBytes(8, seed: 77);
        var hash = new Hash8(bytes);
        var roundtripped = new Hash8(hash.GetBytes());
        roundtripped.Should().Be(hash);
    }

    [Fact]
    public void Ulong_constructor_and_GetBytes_are_consistent()
    {
        var value = 0xDEADBEEFCAFEBABEUL;
        var hashA = new Hash8(value);
        var hashB = new Hash8(hashA.GetBytes());
        hashA.Should().Be(hashB);
    }

    // ---- Equality -----------------------------------------------------------

    [Fact]
    public void Same_bytes_are_equal()
    {
        var bytes = TestUtils.RandomBytes(8, seed: 1);
        var a = new Hash8(bytes);
        var b = new Hash8(bytes);
        a.Should().Be(b);
        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Different_values_are_not_equal()
    {
        var a = new Hash8(0x0000000000000001UL);
        var b = new Hash8(0x0000000000000002UL);
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_is_equal_for_equal_hashes()
    {
        var bytes = TestUtils.RandomBytes(8, seed: 55);
        var a = new Hash8(bytes);
        var b = new Hash8(bytes);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ---- CompareTo ----------------------------------------------------------

    [Fact]
    public void CompareTo_returns_zero_for_equal_hashes()
    {
        var h = new Hash8(0xABCDEF0123456789UL);
        h.CompareTo(h).Should().Be(0);
    }

    [Fact]
    public void CompareTo_orders_numerically()
    {
        var smaller = new Hash8(1UL);
        var larger  = new Hash8(2UL);
        smaller.CompareTo(larger).Should().BeLessThan(0);
        larger.CompareTo(smaller).Should().BeGreaterThan(0);
    }

    // ---- ToHexString is lowercase -------------------------------------------

    [Fact]
    public void ToHexString_is_lowercase()
    {
        var hash = new Hash8(0xABCDEF0123456789UL);
        hash.ToHexString().Should().MatchRegex("^[0-9a-f]{16}$");
    }

    // ---- Property-based -----------------------------------------------------

    [Property(MaxTest = 1000)]
    public void Hex_roundtrip_property(ulong value)
    {
        var hash = new Hash8(value);
        Hash8.FromHexString(hash.ToHexString()).Should().Be(hash);
    }

    [Property(MaxTest = 1000)]
    public void Bytes_roundtrip_property(ulong value)
    {
        var hash = new Hash8(value);
        new Hash8(hash.GetBytes()).Should().Be(hash);
    }
}
