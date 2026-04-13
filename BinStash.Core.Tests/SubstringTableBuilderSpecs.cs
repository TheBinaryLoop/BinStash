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

public class SubstringTableBuilderSpecs
{
    // ---- empty / trivial inputs --------------------------------------------

    [Fact]
    public void Empty_string_produces_no_tokens()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("");
        tokens.Should().BeEmpty();
    }

    [Fact]
    public void String_with_no_separators_produces_single_token_with_sep_none()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("hello");
        tokens.Should().HaveCount(1);
        tokens[0].sep.Should().Be(Separator.None);
        sut.Table[tokens[0].id].Should().Be("hello");
    }

    // ---- single-separator cases -------------------------------------------

    // Separator enum values as chars: '.' '/' '\' ':' '-' '_'
    [Theory]
    [InlineData("a.b", "a", '.', "b")]
    [InlineData("a/b", "a", '/', "b")]
    [InlineData("a\\b", "a", '\\', "b")]
    [InlineData("a:b", "a", ':', "b")]
    [InlineData("a-b", "a", '-', "b")]
    [InlineData("a_b", "a", '_', "b")]
    public void Single_separator_between_two_segments_tokenizes_correctly(
        string input, string left, char sepChar, string right)
    {
        var expectedSep = (Separator)sepChar;
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize(input);

        tokens.Should().HaveCount(2);
        tokens[0].sep.Should().Be(expectedSep);
        tokens[1].sep.Should().Be(Separator.None);
        sut.Table[tokens[0].id].Should().Be(left);
        sut.Table[tokens[1].id].Should().Be(right);
    }

    // ---- consecutive-separator / URL regression tests ---------------------

    [Fact]
    public void Double_slash_produces_empty_segment_before_second_slash()
    {
        // "://" → colon→empty+slash → empty+slash → "something"
        // After the colon, the first slash produces an empty segment before it,
        // and the second slash also produces an empty segment before it.
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("://");

        // Expected: ("", Colon), ("", Slash), ("", Slash) — but the last separator
        // exhausts the string so there is no trailing None token.
        // Actually: i=0 is ':', emit ("",Colon), start=1
        //           i=1 is '/', emit ("",Slash), start=2
        //           i=2 is '/', emit ("",Slash), start=3
        //           loop ends; start==length, no trailing token
        tokens.Should().HaveCount(3);
        sut.Table[tokens[0].id].Should().Be("");
        tokens[0].sep.Should().Be(Separator.Colon);
        sut.Table[tokens[1].id].Should().Be("");
        tokens[1].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[2].id].Should().Be("");
        tokens[2].sep.Should().Be(Separator.Slash);
    }

    [Fact]
    public void Https_url_tokenizes_preserving_double_slash()
    {
        // "https://example.com/path"
        // Expected tokens: ("https",Colon), ("",Slash), ("",Slash),
        //                  ("example",Dot), ("com",Slash), ("path",None)
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("https://example.com/path");

        tokens.Should().HaveCount(6);

        sut.Table[tokens[0].id].Should().Be("https");
        tokens[0].sep.Should().Be(Separator.Colon);

        sut.Table[tokens[1].id].Should().Be("");
        tokens[1].sep.Should().Be(Separator.Slash);

        sut.Table[tokens[2].id].Should().Be("");
        tokens[2].sep.Should().Be(Separator.Slash);

        sut.Table[tokens[3].id].Should().Be("example");
        tokens[3].sep.Should().Be(Separator.Dot);

        sut.Table[tokens[4].id].Should().Be("com");
        tokens[4].sep.Should().Be(Separator.Slash);

        sut.Table[tokens[5].id].Should().Be("path");
        tokens[5].sep.Should().Be(Separator.None);
    }

    [Fact]
    public void Http_url_with_port_tokenizes_correctly()
    {
        // "http://localhost:8080/dashboard"
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("http://localhost:8080/dashboard");

        // ("http",Colon),("",Slash),("",Slash),("localhost",Colon),
        // ("8080",Slash),("dashboard",None)
        tokens.Should().HaveCount(6);
        sut.Table[tokens[0].id].Should().Be("http");
        tokens[0].sep.Should().Be(Separator.Colon);
        sut.Table[tokens[1].id].Should().Be("");
        tokens[1].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[2].id].Should().Be("");
        tokens[2].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[3].id].Should().Be("localhost");
        tokens[3].sep.Should().Be(Separator.Colon);
        sut.Table[tokens[4].id].Should().Be("8080");
        tokens[4].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[5].id].Should().Be("dashboard");
        tokens[5].sep.Should().Be(Separator.None);
    }

    [Fact]
    public void Triple_slash_file_uri_tokenizes_correctly()
    {
        // "file:///path/to/file"
        // ("file",Colon),("",Slash),("",Slash),("",Slash),
        // ("path",Slash),("to",Slash),("file",None)
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("file:///path/to/file");

        tokens.Should().HaveCount(7);
        sut.Table[tokens[0].id].Should().Be("file");
        tokens[0].sep.Should().Be(Separator.Colon);
        sut.Table[tokens[1].id].Should().Be("");
        tokens[1].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[2].id].Should().Be("");
        tokens[2].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[3].id].Should().Be("");
        tokens[3].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[4].id].Should().Be("path");
        tokens[4].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[5].id].Should().Be("to");
        tokens[5].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[6].id].Should().Be("file");
        tokens[6].sep.Should().Be(Separator.None);
    }

    [Fact]
    public void Separator_at_start_of_string_emits_empty_first_segment()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("/leading");

        tokens.Should().HaveCount(2);
        sut.Table[tokens[0].id].Should().Be("");
        tokens[0].sep.Should().Be(Separator.Slash);
        sut.Table[tokens[1].id].Should().Be("leading");
        tokens[1].sep.Should().Be(Separator.None);
    }

    [Fact]
    public void Separator_at_end_of_string_emits_no_trailing_none_token()
    {
        // "trailing/" — after the slash start=9 == length, so no extra token
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("trailing/");

        tokens.Should().HaveCount(1);
        sut.Table[tokens[0].id].Should().Be("trailing");
        tokens[0].sep.Should().Be(Separator.Slash);
    }

    // ---- interning / deduplication ----------------------------------------

    [Fact]
    public void Repeated_segment_text_maps_to_same_table_id()
    {
        var sut = new SubstringTableBuilder();
        var t1 = sut.Tokenize("foo/foo");

        t1[0].id.Should().Be(t1[1].id, "the same text 'foo' should resolve to the same table entry");
    }

    [Fact]
    public void Multiple_tokenize_calls_share_the_same_table()
    {
        var sut = new SubstringTableBuilder();
        var t1 = sut.Tokenize("hello/world");
        var t2 = sut.Tokenize("world/hello");

        // Both calls see "hello" and "world"; ids must be consistent across calls
        t1[0].id.Should().Be(t2[1].id, "'hello' should have the same id in both calls");
        t1[1].id.Should().Be(t2[0].id, "'world' should have the same id in both calls");
    }

    [Fact]
    public void Table_does_not_grow_for_already_seen_segments()
    {
        var sut = new SubstringTableBuilder();
        sut.Tokenize("a.b.c");
        var countAfterFirst = sut.Table.Count;
        sut.Tokenize("a.b.c");
        sut.Table.Count.Should().Be(countAfterFirst);
    }

    // ---- separator character coverage ------------------------------------

    [Fact]
    public void All_separator_characters_are_recognized()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("a.b/c\\d:e-f_g");

        // 7 segments, last has None separator
        tokens.Should().HaveCount(7);
        tokens[0].sep.Should().Be(Separator.Dot);
        tokens[1].sep.Should().Be(Separator.Slash);
        tokens[2].sep.Should().Be(Separator.Backslash);
        tokens[3].sep.Should().Be(Separator.Colon);
        tokens[4].sep.Should().Be(Separator.Dash);
        tokens[5].sep.Should().Be(Separator.Underscore);
        tokens[6].sep.Should().Be(Separator.None);
    }

    // ---- misc edge cases --------------------------------------------------

    [Fact]
    public void Only_separator_chars_produce_all_empty_segments_except_last()
    {
        // "..." → ("",Dot),("",Dot),("",Dot); start=3==length, no trailing None
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("...");

        tokens.Should().HaveCount(3);
        foreach (var t in tokens)
        {
            sut.Table[t.id].Should().Be("");
        }
        tokens[0].sep.Should().Be(Separator.Dot);
        tokens[1].sep.Should().Be(Separator.Dot);
        tokens[2].sep.Should().Be(Separator.Dot);
    }

    [Fact]
    public void Single_separator_char_alone_produces_one_token_with_empty_segment()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize(".");

        tokens.Should().HaveCount(1);
        sut.Table[tokens[0].id].Should().Be("");
        tokens[0].sep.Should().Be(Separator.Dot);
    }

    [Fact]
    public void Non_separator_unicode_chars_are_treated_as_segment_content()
    {
        var sut = new SubstringTableBuilder();
        var tokens = sut.Tokenize("Ä-Ö");

        tokens.Should().HaveCount(2);
        sut.Table[tokens[0].id].Should().Be("Ä");
        tokens[0].sep.Should().Be(Separator.Dash);
        sut.Table[tokens[1].id].Should().Be("Ö");
        tokens[1].sep.Should().Be(Separator.None);
    }
}
