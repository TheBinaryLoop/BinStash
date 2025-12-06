// Copyright (C) 2025  Lukas Eßmann
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
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(ms);
        VarIntUtils.ReadVarInt<ushort>(reader).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_int(int value)
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(ms);
        VarIntUtils.ReadVarInt<int>(reader).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_uint(uint value)
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(ms);
        VarIntUtils.ReadVarInt<uint>(reader).Should().Be(value);
    }
    
    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_long(long value)
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(ms);
        VarIntUtils.ReadVarInt<long>(reader).Should().Be(value);
    }

    [Property(MaxTest = 5000)]
    public void Encode_then_decode_RoundTrips_ulong(ulong value)
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, value);
        ms.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(ms);
        VarIntUtils.ReadVarInt<ulong>(reader).Should().Be(value);
    }
    
    [Property]
    public void Zero_is_encoded_minimally()
    {
        using var ms = new MemoryStream();
        VarIntUtils.WriteVarInt(ms, 0);
        ms.Length.Should().Be(1);
    }
}
