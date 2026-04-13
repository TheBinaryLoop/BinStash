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

using BinStash.Core.Extensions;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class StreamExtensionsSpecs
{
    // ---- Null stream --------------------------------------------------------

    [Fact]
    public async Task Null_stream_throws_ArgumentNullException()
    {
        Stream stream = null!;
        var act = async () => await stream.ToByteArrayAsync();
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---- Non-readable stream ------------------------------------------------

    [Fact]
    public async Task Non_readable_stream_throws_InvalidOperationException()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            // ReSharper disable once AccessToDisposedClosure
            var act = async () => await fs.ToByteArrayAsync();
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Empty stream -------------------------------------------------------

    [Fact]
    public async Task Empty_stream_returns_empty_array()
    {
        using var ms = new MemoryStream();
        var result = await ms.ToByteArrayAsync();
        result.Should().BeEmpty();
    }

    // ---- Non-empty stream ---------------------------------------------------

    [Fact]
    public async Task Stream_with_data_returns_correct_bytes()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var ms = new MemoryStream(data);
        var result = await ms.ToByteArrayAsync();
        result.Should().Equal(data);
    }

    [Fact]
    public async Task Large_stream_returns_all_bytes()
    {
        var data = TestUtils.RandomBytes(256 * 1024, seed: 99);
        using var ms = new MemoryStream(data);
        var result = await ms.ToByteArrayAsync();
        result.Should().Equal(data);
    }

    // ---- Partial stream position --------------------------------------------

    [Fact]
    public async Task Reads_from_current_position_to_end()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var ms = new MemoryStream(data);
        ms.Position = 2; // start from index 2
        var result = await ms.ToByteArrayAsync();
        result.Should().Equal(30, 40, 50);
    }
}
