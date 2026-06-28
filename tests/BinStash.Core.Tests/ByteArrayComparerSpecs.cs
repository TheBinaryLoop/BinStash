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

using BinStash.Core.Helper;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class ByteArrayComparerSpecs
{
    private static readonly ByteArrayComparer Cmp = ByteArrayComparer.Instance;

    // ---- Null handling -------------------------------------------------------

    [Fact]
    public void Both_null_returns_zero()
    {
        Cmp.Compare(null, null).Should().Be(0);
    }

    [Fact]
    public void Left_null_returns_negative()
    {
        Cmp.Compare(null, new byte[1]).Should().BeLessThan(0);
    }

    [Fact]
    public void Right_null_returns_positive()
    {
        Cmp.Compare(new byte[1], null).Should().BeGreaterThan(0);
    }

    // ---- Reference equality -------------------------------------------------

    [Fact]
    public void Same_reference_returns_zero()
    {
        var arr = new byte[] { 1, 2, 3 };
        Cmp.Compare(arr, arr).Should().Be(0);
    }

    // ---- Equal arrays -------------------------------------------------------

    [Fact]
    public void Equal_content_returns_zero()
    {
        Cmp.Compare(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }).Should().Be(0);
    }

    [Fact]
    public void Empty_arrays_are_equal()
    {
        Cmp.Compare(Array.Empty<byte>(), Array.Empty<byte>()).Should().Be(0);
    }

    // ---- Ordering by first differing byte -----------------------------------

    [Theory]
    [InlineData(new byte[] { 0 }, new byte[] { 1 })]
    [InlineData(new byte[] { 1, 0 }, new byte[] { 1, 1 })]
    [InlineData(new byte[] { 0xFF }, new byte[] { 0xFF, 0x00 })] // shorter = lexicographically less when prefix equal
    public void Left_less_than_right_returns_negative(byte[] left, byte[] right)
    {
        Cmp.Compare(left, right).Should().BeLessThan(0);
    }

    [Theory]
    [InlineData(new byte[] { 2 }, new byte[] { 1 })]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 1, 1 })]
    public void Left_greater_than_right_returns_positive(byte[] left, byte[] right)
    {
        Cmp.Compare(left, right).Should().BeGreaterThan(0);
    }

    // ---- Length comparison when prefix is equal -----------------------------

    [Fact]
    public void Longer_array_with_equal_prefix_is_greater()
    {
        Cmp.Compare(new byte[] { 1, 2, 3 }, new byte[] { 1, 2 }).Should().BeGreaterThan(0);
        Cmp.Compare(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }).Should().BeLessThan(0);
    }

    // ---- Antisymmetry -------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 1 }, new byte[] { 2 })]
    [InlineData(new byte[] { 5, 0 }, new byte[] { 5, 1 })]
    public void Comparison_is_antisymmetric(byte[] a, byte[] b)
    {
        var fwd = Cmp.Compare(a, b);
        var rev = Cmp.Compare(b, a);
        fwd.Should().BeLessThan(0);
        rev.Should().BeGreaterThan(0);
    }

    // ---- Transitivity (spot-check) ------------------------------------------

    [Fact]
    public void Comparison_is_transitive()
    {
        byte[] a = [1];
        byte[] b = [2];
        byte[] c = [3];
        Cmp.Compare(a, b).Should().BeLessThan(0);
        Cmp.Compare(b, c).Should().BeLessThan(0);
        Cmp.Compare(a, c).Should().BeLessThan(0);
    }

    // ---- Usable as sort key -------------------------------------------------

    [Fact]
    public void Sort_produces_lexicographic_order()
    {
        var list = new List<byte[]>
        {
            new byte[] { 3 },
            new byte[] { 1 },
            new byte[] { 2 },
            new byte[] { 1, 0 },
        };
        list.Sort(Cmp);

        list[0].Should().Equal(1);
        list[1].Should().Equal(1, 0);
        list[2].Should().Equal(2);
        list[3].Should().Equal(3);
    }
}
