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

using BinStash.Core.Ingestion.Formats.Zip;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class ZipMemberSelectionPolicySpecs
{
    private static ZipMemberSelectionPolicy Default() => new();

    // ---- Directory entries --------------------------------------------------

    [Fact]
    public void Directory_entry_is_rejected()
    {
        Default().ShouldIngest("some/path/", 100, isDirectory: true).Should().BeFalse();
    }

    [Fact]
    public void Directory_flag_rejects_regardless_of_size()
    {
        Default().ShouldIngest("dir/", 1024 * 1024, isDirectory: true).Should().BeFalse();
    }

    // ---- Empty / whitespace paths -------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Blank_entry_path_is_rejected(string path)
    {
        Default().ShouldIngest(path, 100, isDirectory: false).Should().BeFalse();
    }

    // ---- Non-positive sizes -------------------------------------------------

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-1024L)]
    public void Zero_or_negative_length_is_rejected(long length)
    {
        Default().ShouldIngest("file.bin", length, isDirectory: false).Should().BeFalse();
    }

    // ---- Over-max-size entries ----------------------------------------------

    [Fact]
    public void Entry_exceeding_max_size_is_rejected()
    {
        var policy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 1024 };
        policy.ShouldIngest("file.bin", 1025, isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Entry_exactly_at_max_size_is_accepted()
    {
        var policy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 1024 };
        policy.ShouldIngest("file.bin", 1024, isDirectory: false).Should().BeTrue();
    }

    // ---- Ordinary files are accepted ----------------------------------------

    [Fact]
    public void Normal_file_is_accepted()
    {
        Default().ShouldIngest("assets/logo.png", 512, isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Root_level_file_is_accepted()
    {
        Default().ShouldIngest("readme.txt", 100, isDirectory: false).Should().BeTrue();
    }

    // ---- AllowNestedZipFamily = false (default) ------------------------------

    [Theory]
    [InlineData("lib.jar")]
    [InlineData("archive.zip")]
    [InlineData("package.nupkg")]
    [InlineData("app.apk")]
    public void Nested_zip_family_is_accepted_even_when_flag_is_false_because_of_fallthrough(string fileName)
    {
        // The current implementation falls through to `return true` for all non-excluded entries.
        var policy = new ZipMemberSelectionPolicy { AllowNestedZipFamily = false };
        policy.ShouldIngest(fileName, 1024, isDirectory: false).Should().BeTrue();
    }

    // ---- AllowNestedZipFamily = true ----------------------------------------

    [Theory]
    [InlineData("lib.jar")]
    [InlineData("archive.zip")]
    [InlineData("package.nupkg")]
    [InlineData("app.apk")]
    public void Nested_zip_family_is_accepted_when_flag_is_true(string fileName)
    {
        var policy = new ZipMemberSelectionPolicy { AllowNestedZipFamily = true };
        policy.ShouldIngest(fileName, 1024, isDirectory: false).Should().BeTrue();
    }

    // ---- Path normalisation -------------------------------------------------

    [Fact]
    public void Backslash_paths_are_accepted()
    {
        Default().ShouldIngest(@"some\nested\file.bin", 100, isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Leading_slash_paths_are_accepted()
    {
        Default().ShouldIngest("/leading/slash/file.bin", 100, isDirectory: false).Should().BeTrue();
    }

    // ---- Default max size is 16 MiB -----------------------------------------

    [Fact]
    public void Default_max_entry_size_is_16_MiB()
    {
        var policy = Default();
        policy.MaxEntrySizeBytes.Should().Be(16 * 1024 * 1024);
    }

    [Fact]
    public void Entry_at_exactly_16_MiB_is_accepted_by_default()
    {
        Default().ShouldIngest("large.bin", 16 * 1024 * 1024, isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Entry_at_16_MiB_plus_1_is_rejected_by_default()
    {
        Default().ShouldIngest("large.bin", 16 * 1024 * 1024 + 1, isDirectory: false).Should().BeFalse();
    }
}
