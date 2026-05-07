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

public class Hash32Specs
{
    // ---- Construction guards ------------------------------------------------

    [Fact]
    public void Constructor_throws_for_wrong_byte_length()
    {
        var act = () => new Hash32(new byte[31]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_accepts_exactly_32_bytes()
    {
        var hash = new Hash32(new byte[32]);
        hash.Should().BeOfType<Hash32>();
    }

    [Fact]
    public void Span_constructor_throws_for_wrong_length()
    {
        var bytes = new byte[33];
        // Cannot capture ReadOnlySpan<byte> in a lambda; use a local helper
        FluentAssertions.FluentActions
            .Invoking(() => new Hash32(new ReadOnlySpan<byte>(bytes)))
            .Should().Throw<ArgumentException>();
    }

    // ---- FromHexString -------------------------------------------------------

    [Fact]
    public void FromHexString_throws_for_wrong_length()
    {
        var act = () => Hash32.FromHexString("abc");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromHexString_accepts_64_char_hex()
    {
        var hex = new string('0', 64);
        var hash = Hash32.FromHexString(hex);
        hash.ToHexString().Should().Be(hex);
    }

    // ---- Round-trip hex / bytes ---------------------------------------------

    [Fact]
    public void ToHexString_FromHexString_roundtrip()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 7);
        var hash = new Hash32(bytes);
        var hex = hash.ToHexString();
        Hash32.FromHexString(hex).Should().Be(hash);
    }

    [Fact]
    public void GetBytes_roundtrips_to_equal_hash()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 42);
        var hash = new Hash32(bytes);
        var roundtripped = new Hash32(hash.GetBytes());
        roundtripped.Should().Be(hash);
    }

    [Fact]
    public void WriteBytes_roundtrips_to_equal_hash()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 13);
        var original = new Hash32(bytes);
        Span<byte> dest = stackalloc byte[32];
        original.WriteBytes(dest);
        new Hash32(dest).Should().Be(original);
    }

    [Fact]
    public void WriteBytes_throws_when_destination_too_small()
    {
        var hash = new Hash32(new byte[32]);
        var act = () =>
        {
            Span<byte> small = stackalloc byte[31];
            hash.WriteBytes(small);
        };
        act.Should().Throw<ArgumentException>();
    }

    // ---- Equality -----------------------------------------------------------

    [Fact]
    public void Same_bytes_are_equal()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 1);
        var a = new Hash32(bytes);
        var b = new Hash32(bytes);
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Different_bytes_are_not_equal()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 2);
        var a = new Hash32(bytes);
        var b = new Hash32(TestUtils.WithBitFlip(bytes, 0));
        a.Should().NotBe(b);
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void All_zero_bytes_produce_equal_hashes()
    {
        var a = new Hash32(new byte[32]);
        var b = new Hash32(new byte[32]);
        a.Should().Be(b);
    }

    [Fact]
    public void GetHashCode_is_equal_for_equal_hashes()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 5);
        var a = new Hash32(bytes);
        var b = new Hash32(bytes);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ---- CompareTo ----------------------------------------------------------

    [Fact]
    public void CompareTo_returns_zero_for_equal_hashes()
    {
        var bytes = TestUtils.RandomBytes(32, seed: 3);
        var a = new Hash32(bytes);
        var b = new Hash32(bytes);
        a.CompareTo(b).Should().Be(0);
    }

    [Fact]
    public void CompareTo_is_consistent_with_first_differing_byte()
    {
        var bytes = new byte[32];
        bytes[0] = 0x00;
        var smaller = new Hash32(bytes);

        bytes[0] = 0x01;
        var larger = new Hash32(bytes);

        smaller.CompareTo(larger).Should().BeLessThan(0);
        larger.CompareTo(smaller).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_is_antisymmetric()
    {
        var a = new Hash32(TestUtils.RandomBytes(32, seed: 10));
        var b = new Hash32(TestUtils.RandomBytes(32, seed: 11));
        var cmp = a.CompareTo(b);
        if (cmp == 0)
            b.CompareTo(a).Should().Be(0);
        else if (cmp < 0)
            b.CompareTo(a).Should().BeGreaterThan(0);
        else
            b.CompareTo(a).Should().BeLessThan(0);
    }

    // ---- ToHexString is lowercase -------------------------------------------

    [Fact]
    public void ToHexString_is_lowercase()
    {
        var bytes = new byte[32];
        bytes[0] = 0xAB;
        bytes[1] = 0xCD;
        var hash = new Hash32(bytes);
        hash.ToHexString().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    // ---- Byte preservation --------------------------------------------------

    [Fact]
    public void All_bytes_are_preserved_round_trip()
    {
        // Every position matters
        for (var i = 0; i < 32; i++)
        {
            var bytes = new byte[32];
            bytes[i] = 0xFF;
            var hash = new Hash32(bytes);
            hash.GetBytes()[i].Should().Be(0xFF);
        }
    }

    // ---- Property-based -----------------------------------------------------

    [Property(MaxTest = 1000)]
    public void Hex_roundtrip_property(byte b0, byte b1, byte b2, byte b3)
    {
        // Build a deterministic 32-byte input from 4 seed bytes
        var bytes = new byte[32];
        bytes[0] = b0; bytes[1] = b1; bytes[2] = b2; bytes[3] = b3;
        var hash = new Hash32(bytes);
        Hash32.FromHexString(hash.ToHexString()).Should().Be(hash);
    }
}
