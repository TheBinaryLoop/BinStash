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

using System.Text.Json;
using BinStash.Core.Extensions;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class DictionaryExtensionsSpecs
{
    // ---- Empty dictionary ---------------------------------------------------

    [Fact]
    public void Empty_dictionary_serialises_to_empty_json_object()
    {
        var dict = new Dictionary<string, string>();
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Should().BeEmpty();
    }

    // ---- Flat keys ----------------------------------------------------------

    [Fact]
    public void Flat_key_appears_at_root_level()
    {
        var dict = new Dictionary<string, string> { ["name"] = "Alice" };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("name").GetString().Should().Be("Alice");
    }

    [Fact]
    public void Multiple_flat_keys_all_appear_at_root()
    {
        var dict = new Dictionary<string, string>
        {
            ["a"] = "1",
            ["b"] = "2",
        };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("a").GetString().Should().Be("1");
        doc.RootElement.GetProperty("b").GetString().Should().Be("2");
    }

    // ---- Colon-separated nesting --------------------------------------------

    [Fact]
    public void Two_part_key_creates_one_level_of_nesting()
    {
        var dict = new Dictionary<string, string> { ["outer:inner"] = "value" };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement
            .GetProperty("outer")
            .GetProperty("inner")
            .GetString()
            .Should().Be("value");
    }

    [Fact]
    public void Three_part_key_creates_two_levels_of_nesting()
    {
        var dict = new Dictionary<string, string> { ["a:b:c"] = "deep" };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement
            .GetProperty("a")
            .GetProperty("b")
            .GetProperty("c")
            .GetString()
            .Should().Be("deep");
    }

    [Fact]
    public void Sibling_keys_under_same_parent_are_both_present()
    {
        var dict = new Dictionary<string, string>
        {
            ["parent:child1"] = "v1",
            ["parent:child2"] = "v2",
        };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        var parent = doc.RootElement.GetProperty("parent");
        parent.GetProperty("child1").GetString().Should().Be("v1");
        parent.GetProperty("child2").GetString().Should().Be("v2");
    }

    // ---- Scalar overwrite by nested key -------------------------------------

    [Fact]
    public void Later_nested_key_overwrites_earlier_scalar_at_same_path()
    {
        // First "a" = "scalar", then "a:b" = "nested" should overwrite "a" with a dict
        var dict = new Dictionary<string, string>
        {
            ["a"]   = "scalar",
            ["a:b"] = "nested",
        };
        var json = dict.ToJson();
        using var doc = JsonDocument.Parse(json);
        // "a" should now be an object with key "b"
        doc.RootElement.GetProperty("a").ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.GetProperty("a").GetProperty("b").GetString().Should().Be("nested");
    }

    // ---- Output is valid JSON -----------------------------------------------

    [Fact]
    public void Output_is_always_valid_json()
    {
        var dict = new Dictionary<string, string>
        {
            ["x:y:z"] = "hello",
            ["x:y:w"] = "world",
            ["top"]   = "level",
        };
        var json = dict.ToJson();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    // ---- Indented output ----------------------------------------------------

    [Fact]
    public void Output_is_indented()
    {
        var dict = new Dictionary<string, string> { ["key"] = "val" };
        var json = dict.ToJson();
        // Indented JSON will contain newlines
        json.Should().Contain(Environment.NewLine);
    }
}
