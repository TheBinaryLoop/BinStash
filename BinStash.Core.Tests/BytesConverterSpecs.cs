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

public class BytesConverterSpecs
{
    // ---- ConvertBytesToKB ---------------------------------------------------

    [Theory]
    [InlineData(1024L, 1.0)]
    [InlineData(2048L, 2.0)]
    [InlineData(0L,    0.0)]
    public void ConvertBytesToKB_divides_by_1024(long bytes, double expected)
    {
        BytesConverter.ConvertBytesToKB(bytes).Should().BeApproximately(expected, precision: 0.001);
    }

    // ---- ConvertBytesToMB ---------------------------------------------------

    [Theory]
    [InlineData(1024L * 1024L,      1.0)]
    [InlineData(1024L * 1024L * 4L, 4.0)]
    [InlineData(0L,                 0.0)]
    public void ConvertBytesToMB_divides_by_1024_twice(long bytes, double expected)
    {
        BytesConverter.ConvertBytesToMB(bytes).Should().BeApproximately(expected, precision: 0.001);
    }

    // ---- ConvertBytesToGB ---------------------------------------------------

    [Fact]
    public void ConvertBytesToGB_one_gb()
    {
        BytesConverter.ConvertBytesToGB(1024L * 1024L * 1024L).Should().BeApproximately(1.0, precision: 0.001);
    }

    // ---- ConvertBytesToTB ---------------------------------------------------

    [Fact]
    public void ConvertBytesToTB_one_tb()
    {
        BytesConverter.ConvertBytesToTB(1024L * 1024L * 1024L * 1024L).Should().BeApproximately(1.0, precision: 0.001);
    }

    // ---- ConvertBytesToPB ---------------------------------------------------

    [Fact]
    public void ConvertBytesToPB_one_pb()
    {
        var onePb = 1024L * 1024L * 1024L * 1024L * 1024L;
        BytesConverter.ConvertBytesToPB(onePb).Should().BeApproximately(1.0, precision: 0.001);
    }

    // ---- BytesToHuman -------------------------------------------------------

    [Fact]
    public void BytesToHuman_zero_returns_zero_Mb()
    {
        BytesConverter.BytesToHuman(0).Should().Be("0 Mb");
    }

    [Theory]
    [InlineData(1L,    "byte")]
    [InlineData(512L,  "byte")]
    [InlineData(1023L, "byte")]
    public void BytesToHuman_sub_KB_shows_bytes(long value, string unit)
    {
        BytesConverter.BytesToHuman(value).Should().EndWith(unit);
    }

    [Theory]
    [InlineData(1024L,           "Kb")]
    [InlineData(1024L * 512L,    "Kb")]
    [InlineData(1024L * 1023L,   "Kb")]
    public void BytesToHuman_KB_range_shows_Kb(long value, string unit)
    {
        BytesConverter.BytesToHuman(value).Should().EndWith(unit);
    }

    [Theory]
    [InlineData(1024L * 1024L,          "Mb")]
    [InlineData(1024L * 1024L * 512L,   "Mb")]
    public void BytesToHuman_MB_range_shows_Mb(long value, string unit)
    {
        BytesConverter.BytesToHuman(value).Should().EndWith(unit);
    }

    [Theory]
    [InlineData(1024L * 1024L * 1024L,           "Gb")]
    [InlineData(1024L * 1024L * 1024L * 512L,    "Gb")]
    public void BytesToHuman_GB_range_shows_Gb(long value, string unit)
    {
        BytesConverter.BytesToHuman(value).Should().EndWith(unit);
    }

    [Fact]
    public void BytesToHuman_1TB_shows_Tb()
    {
        BytesConverter.BytesToHuman(1024L * 1024L * 1024L * 1024L).Should().EndWith("Tb");
    }

    [Fact]
    public void BytesToHuman_1PB_shows_Pb()
    {
        BytesConverter.BytesToHuman(1024L * 1024L * 1024L * 1024L * 1024L).Should().EndWith("Pb");
    }

    [Fact]
    public void BytesToHuman_1EB_shows_Eb()
    {
        // 1 EB = 2^60
        BytesConverter.BytesToHuman(1024L * 1024L * 1024L * 1024L * 1024L * 1024L).Should().EndWith("Eb");
    }

    // ---- FloatForm ----------------------------------------------------------

    [Fact]
    public void FloatForm_zero_returns_empty_string_via_custom_format()
    {
        // ##.## with value 0 produces empty string (the ## format suppresses insignificant zeros)
        BytesConverter.FloatForm(0.0).Should().Be(string.Empty);
    }

    [Fact]
    public void FloatForm_integer_value_returns_no_decimal_point()
    {
        BytesConverter.FloatForm(1.0).Should().Be("1");
    }

    [Fact]
    public void FloatForm_rounds_to_at_most_two_decimal_places()
    {
        // 1.256 rounds to 1.26; the format is culture-sensitive, so just verify
        // the result is parseable as a number equal to the rounded value.
        var result = BytesConverter.FloatForm(1.256);
        var parsed = double.Parse(result, System.Globalization.CultureInfo.CurrentCulture);
        parsed.Should().BeApproximately(1.26, precision: 0.001);
    }

    [Fact]
    public void FloatForm_large_value_returns_no_trailing_zeros()
    {
        // 100.0 -> "100" (no unnecessary decimal places)
        BytesConverter.FloatForm(100.0).Should().Be("100");
    }
}
