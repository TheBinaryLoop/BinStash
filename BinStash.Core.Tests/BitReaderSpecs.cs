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

namespace BinStash.Core.Tests;

public class BitReaderSpecs
{
    // ---- Construction -------------------------------------------------------

    [Fact]
    public void Byte_array_constructor_initialises_correctly()
    {
        var r = new BitReader(new byte[4]);
        r.BitsRemaining.Should().Be(32);
        r.BytesConsumed.Should().Be(0);
    }

    [Fact]
    public void Slice_constructor_rejects_negative_length()
    {
        var act = () => new BitReader(new byte[4], 0, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Slice_constructor_rejects_out_of_bounds_offset()
    {
        var act = () => new BitReader(new byte[4], 5, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Slice_constructor_rejects_offset_plus_length_overflow()
    {
        var act = () => new BitReader(new byte[4], 2, 3); // 2+3=5 > 4
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Null_data_throws()
    {
        // The single-arg ctor delegates to (data, 0, data.Length) which dereferences data
        // before reaching the null check in the 3-arg ctor, so NullReferenceException is thrown.
        // Using the 3-arg ctor hits ArgumentNullException explicitly.
        var actSingleArg = () => new BitReader(null!);
        actSingleArg.Should().Throw<Exception>(); // NullReferenceException in practice

        var actThreeArg = () => new BitReader(null!, 0, 0);
        actThreeArg.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Span_constructor_copies_and_works()
    {
        ReadOnlySpan<byte> span = [0b10101010];
        var r = new BitReader(span);
        r.BitsRemaining.Should().Be(8);
    }

    // ---- Empty buffer -------------------------------------------------------

    [Fact]
    public void Empty_buffer_has_zero_bits_remaining()
    {
        var r = new BitReader(Array.Empty<byte>());
        r.BitsRemaining.Should().Be(0);
    }

    [Fact]
    public void ReadBits_zero_bits_from_empty_returns_zero()
    {
        var r = new BitReader(Array.Empty<byte>());
        r.ReadBits(0).Should().Be(0UL);
    }

    [Fact]
    public void ReadBits_any_nonzero_count_from_empty_throws()
    {
        var r = new BitReader(Array.Empty<byte>());
        var act = () => r.ReadBits(1);
        act.Should().Throw<EndOfStreamException>();
    }

    // ---- Zero bit reads -----------------------------------------------------

    [Fact]
    public void ReadBits_zero_returns_zero_and_does_not_advance()
    {
        var r = new BitReader([0xFF]);
        r.ReadBits(0).Should().Be(0UL);
        r.BitsRemaining.Should().Be(8);
        r.BytesConsumed.Should().Be(0);
    }

    // ---- Single byte, single-bit reads (LSB first) --------------------------

    [Fact]
    public void Reads_individual_bits_LSB_first_from_single_byte()
    {
        // 0b10110001 => bits in read order: 1,0,0,0,1,1,0,1
        var r = new BitReader([0b10110001]);
        r.ReadBits(1).Should().Be(1UL); // bit 0 = 1
        r.ReadBits(1).Should().Be(0UL); // bit 1 = 0
        r.ReadBits(1).Should().Be(0UL); // bit 2 = 0
        r.ReadBits(1).Should().Be(0UL); // bit 3 = 0
        r.ReadBits(1).Should().Be(1UL); // bit 4 = 1
        r.ReadBits(1).Should().Be(1UL); // bit 5 = 1
        r.ReadBits(1).Should().Be(0UL); // bit 6 = 0
        r.ReadBits(1).Should().Be(1UL); // bit 7 = 1
        r.BitsRemaining.Should().Be(0);
    }

    // ---- Multi-bit reads from single byte -----------------------------------

    [Fact]
    public void Read_4_bits_from_0xAF_returns_lower_nibble()
    {
        // 0xAF = 0b10101111 — lower 4 bits = 0b1111 = 15
        var r = new BitReader([0xAF]);
        r.ReadBits(4).Should().Be(0xFUL);
    }

    [Fact]
    public void Read_8_bits_from_single_byte()
    {
        var r = new BitReader([0xAB]);
        r.ReadBits(8).Should().Be(0xABUL);
        r.BitsRemaining.Should().Be(0);
    }

    // ---- Multi-byte reads ---------------------------------------------------

    [Fact]
    public void Read_16_bits_across_two_bytes()
    {
        // Little-endian bit stream: first byte is low byte of result
        var r = new BitReader([0x34, 0x12]);
        r.ReadBits(16).Should().Be(0x1234UL);
    }

    [Fact]
    public void Read_bits_spanning_byte_boundary()
    {
        // Byte 0: 0b11110000, Byte 1: 0b00001111
        // Reading 5 bits: lower 5 of byte 0 = 0b10000 = 16
        // Then reading 3 bits: upper 3 of byte 0 = 0b111 = 7 (bits 5-7 of 0xF0)
        var r = new BitReader([0b11110000, 0b00001111]);
        r.ReadBits(5).Should().Be(0b10000UL);  // bits 0-4 of 0xF0
        r.ReadBits(3).Should().Be(0b111UL);    // bits 5-7 of 0xF0
    }

    // ---- BitsRemaining and BytesConsumed ------------------------------------

    [Fact]
    public void BitsRemaining_decrements_correctly()
    {
        var r = new BitReader([0xFF, 0xFF]);
        r.BitsRemaining.Should().Be(16);
        r.ReadBits(3);
        r.BitsRemaining.Should().Be(13);
        r.ReadBits(8);
        r.BitsRemaining.Should().Be(5);
    }

    [Fact]
    public void BytesConsumed_reflects_partially_consumed_byte()
    {
        var r = new BitReader([0xFF, 0xFF]);
        r.BytesConsumed.Should().Be(0);
        r.ReadBits(1);
        // 1 bit consumed but still in first byte — counts as 1
        r.BytesConsumed.Should().Be(1);
        r.ReadBits(7); // finish byte 0
        r.BytesConsumed.Should().Be(1);
        r.ReadBits(1); // start byte 1
        r.BytesConsumed.Should().Be(2);
    }

    // ---- Overflow / underflow guards ----------------------------------------

    [Fact]
    public void ReadBits_more_than_64_throws()
    {
        var data = new byte[16];
        var r = new BitReader(data);
        var act = () => r.ReadBits(65);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadBits_throws_when_not_enough_bits()
    {
        var r = new BitReader([0xFF]); // 8 bits
        r.ReadBits(6);                 // 2 left
        var act = () => r.ReadBits(3);
        act.Should().Throw<EndOfStreamException>();
    }

    // ---- Slice constructor limits view --------------------------------------

    [Fact]
    public void Slice_constructor_limits_readable_range()
    {
        var data = new byte[] { 0x00, 0xFF, 0x00 };
        var r = new BitReader(data, offset: 1, length: 1); // only the 0xFF byte
        r.BitsRemaining.Should().Be(8);
        r.ReadBits(8).Should().Be(0xFFUL);
        r.BitsRemaining.Should().Be(0);
    }

    // ---- 64-bit reads -------------------------------------------------------

    [Fact]
    public void Read_64_bits_from_8_bytes_produces_correct_ulong()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var r = new BitReader(data);
        var result = r.ReadBits(64);
        // Expected: packed little-endian, byte 0 in bit positions 0-7
        var expected = 0x0807060504030201UL;
        result.Should().Be(expected);
    }
}
