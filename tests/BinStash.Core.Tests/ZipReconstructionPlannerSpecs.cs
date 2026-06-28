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

using BinStash.Contracts.Release;
using BinStash.Core.Ingestion.Formats.Zip;
using BinStash.Core.Ingestion.Models;
using FluentAssertions;

namespace BinStash.Core.Tests;

public class ZipReconstructionPlannerSpecs
{
    // ---- factory helpers ----------------------------------------------------

    private static ZipReconstructionPlanner DefaultPlanner()
        => new(new ZipMemberSelectionPolicy());

    private static InputItem MakeInput(string absolutePath)
        => new(
            AbsolutePath: absolutePath,
            RelativePath: "component/" + Path.GetFileName(absolutePath),
            RelativePathWithinComponent: Path.GetFileName(absolutePath),
            Component: new Component { Name = "test-component" },
            Length: 1024,
            LastWriteTimeUtc: DateTimeOffset.UtcNow);

    private static ZipArchiveEntryInfo MakeEntry(string fullName, long size = 512, bool isDirectory = false)
        => new(
            FullName: fullName,
            Name: Path.GetFileName(fullName),
            UncompressedLength: size,
            CompressedLength: size / 2,
            IsDirectory: isDirectory,
            Index: 0);

    private static IReadOnlyList<ZipArchiveEntryInfo> SomeEntries()
        => [MakeEntry("lib/foo.dll"), MakeEntry("readme.txt"), MakeEntry("config.json")];

    // ---- Byte-perfect formats: .apk -----------------------------------------

    [Fact]
    public void Apk_file_forces_opaque_storage()
    {
        var result = DefaultPlanner().Plan(MakeInput("release.apk"), SomeEntries());
        result.StoreOpaque.Should().BeTrue();
    }

    [Fact]
    public void Apk_file_sets_RequiresBytePerfect_to_true()
    {
        var result = DefaultPlanner().Plan(MakeInput("app.apk"), SomeEntries());
        result.RequiresBytePerfect.Should().BeTrue();
    }

    [Fact]
    public void Apk_file_sets_reconstruction_kind_to_None()
    {
        var result = DefaultPlanner().Plan(MakeInput("app.apk"), SomeEntries());
        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    [Fact]
    public void Apk_file_selected_entries_list_is_empty()
    {
        var result = DefaultPlanner().Plan(MakeInput("app.apk"), SomeEntries());
        result.SelectedEntries.Should().BeEmpty();
    }

    // ---- Byte-perfect formats: .jar -----------------------------------------

    [Fact]
    public void Jar_file_forces_opaque_storage()
    {
        var result = DefaultPlanner().Plan(MakeInput("library.jar"), SomeEntries());
        result.StoreOpaque.Should().BeTrue();
    }

    [Fact]
    public void Jar_file_sets_RequiresBytePerfect_to_true()
    {
        var result = DefaultPlanner().Plan(MakeInput("library.jar"), SomeEntries());
        result.RequiresBytePerfect.Should().BeTrue();
    }

    [Fact]
    public void Jar_file_sets_reconstruction_kind_to_None()
    {
        var result = DefaultPlanner().Plan(MakeInput("library.jar"), SomeEntries());
        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    // ---- Byte-perfect formats: .nupkg ---------------------------------------

    [Fact]
    public void Nupkg_file_forces_opaque_storage()
    {
        var result = DefaultPlanner().Plan(MakeInput("package.nupkg"), SomeEntries());
        result.StoreOpaque.Should().BeTrue();
    }

    [Fact]
    public void Nupkg_file_sets_RequiresBytePerfect_to_true()
    {
        var result = DefaultPlanner().Plan(MakeInput("package.nupkg"), SomeEntries());
        result.RequiresBytePerfect.Should().BeTrue();
    }

    [Fact]
    public void Nupkg_file_sets_reconstruction_kind_to_None()
    {
        var result = DefaultPlanner().Plan(MakeInput("package.nupkg"), SomeEntries());
        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    // ---- Extension matching is case-insensitive -----------------------------

    [Theory]
    [InlineData("APP.APK")]
    [InlineData("App.Apk")]
    [InlineData("LIBRARY.JAR")]
    [InlineData("Library.Jar")]
    [InlineData("PACKAGE.NUPKG")]
    [InlineData("Package.Nupkg")]
    public void Byte_perfect_format_detection_is_case_insensitive(string filename)
    {
        var result = DefaultPlanner().Plan(MakeInput(filename), SomeEntries());
        result.StoreOpaque.Should().BeTrue();
        result.RequiresBytePerfect.Should().BeTrue();
    }

    // ---- Opaque storage when all entries are filtered out -------------------

    [Fact]
    public void All_entries_filtered_by_policy_produces_opaque_storage()
    {
        // Use a policy that rejects everything (zero max size = everything > 0 is rejected)
        var restrictivePolicy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 0 };
        var planner = new ZipReconstructionPlanner(restrictivePolicy);
        var entries = new[] { MakeEntry("file.dll", 1) };

        var result = planner.Plan(MakeInput("archive.zip"), entries);

        result.StoreOpaque.Should().BeTrue();
    }

    [Fact]
    public void All_entries_filtered_sets_RequiresBytePerfect_to_false()
    {
        var restrictivePolicy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 0 };
        var planner = new ZipReconstructionPlanner(restrictivePolicy);

        var result = planner.Plan(MakeInput("archive.zip"), [MakeEntry("file.dll", 1)]);

        result.RequiresBytePerfect.Should().BeFalse();
    }

    [Fact]
    public void All_entries_filtered_sets_reconstruction_kind_to_None()
    {
        var restrictivePolicy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 0 };
        var planner = new ZipReconstructionPlanner(restrictivePolicy);

        var result = planner.Plan(MakeInput("archive.zip"), [MakeEntry("file.dll", 1)]);

        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    [Fact]
    public void Empty_entries_list_produces_opaque_storage()
    {
        var result = DefaultPlanner().Plan(MakeInput("archive.zip"), []);
        result.StoreOpaque.Should().BeTrue();
        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    [Fact]
    public void Only_directory_entries_produces_opaque_storage()
    {
        var entries = new[]
        {
            MakeEntry("subdir/", 0, isDirectory: true),
            MakeEntry("nested/dir/", 0, isDirectory: true)
        };

        var result = DefaultPlanner().Plan(MakeInput("archive.zip"), entries);

        result.StoreOpaque.Should().BeTrue();
        result.ReconstructionKind.Should().Be(ReconstructionKind.None);
    }

    // ---- Semantic reconstruction when entries are selected ------------------

    [Fact]
    public void Normal_zip_with_selected_entries_uses_semantic_reconstruction()
    {
        var result = DefaultPlanner().Plan(MakeInput("bundle.zip"), SomeEntries());
        result.ReconstructionKind.Should().Be(ReconstructionKind.Semantic);
    }

    [Fact]
    public void Normal_zip_with_selected_entries_does_not_store_opaque()
    {
        var result = DefaultPlanner().Plan(MakeInput("bundle.zip"), SomeEntries());
        result.StoreOpaque.Should().BeFalse();
    }

    [Fact]
    public void Normal_zip_sets_RequiresBytePerfect_to_false()
    {
        var result = DefaultPlanner().Plan(MakeInput("bundle.zip"), SomeEntries());
        result.RequiresBytePerfect.Should().BeFalse();
    }

    [Fact]
    public void Selected_entries_contain_only_accepted_members()
    {
        // Policy accepts everything by default; mix of file and directory entries
        var entries = new[]
        {
            MakeEntry("src/", 0, isDirectory: true),    // directory — rejected by policy
            MakeEntry("src/foo.cs", 512),                // file — accepted
            MakeEntry("src/bar.cs", 256),                // file — accepted
        };

        var result = DefaultPlanner().Plan(MakeInput("archive.zip"), entries);

        result.SelectedEntries.Should().HaveCount(2);
        result.SelectedEntries.Select(e => e.FullName).Should().Contain("src/foo.cs");
        result.SelectedEntries.Select(e => e.FullName).Should().Contain("src/bar.cs");
    }

    [Fact]
    public void Selected_entries_exclude_entries_over_max_size()
    {
        var policy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 100 };
        var planner = new ZipReconstructionPlanner(policy);
        var entries = new[]
        {
            MakeEntry("small.bin", 50),   // under limit — accepted
            MakeEntry("large.bin", 200),  // over limit — rejected
        };

        var result = planner.Plan(MakeInput("data.zip"), entries);

        result.SelectedEntries.Should().HaveCount(1);
        result.SelectedEntries[0].FullName.Should().Be("small.bin");
    }

    [Fact]
    public void Single_selected_entry_produces_semantic_result()
    {
        var entries = new[] { MakeEntry("single.txt", 100) };
        var result = DefaultPlanner().Plan(MakeInput("archive.zip"), entries);

        result.ReconstructionKind.Should().Be(ReconstructionKind.Semantic);
        result.StoreOpaque.Should().BeFalse();
        result.SelectedEntries.Should().HaveCount(1);
    }

    // ---- Reason is always set -----------------------------------------------

    [Fact]
    public void Byte_perfect_result_has_non_empty_reason()
    {
        var result = DefaultPlanner().Plan(MakeInput("app.apk"), SomeEntries());
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Opaque_no_entries_result_has_non_empty_reason()
    {
        var policy = new ZipMemberSelectionPolicy { MaxEntrySizeBytes = 0 };
        var planner = new ZipReconstructionPlanner(policy);
        var result = planner.Plan(MakeInput("archive.zip"), [MakeEntry("file.bin", 1)]);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Semantic_result_has_non_empty_reason()
    {
        var result = DefaultPlanner().Plan(MakeInput("bundle.zip"), SomeEntries());
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    // ---- Non-zip extension falls through to semantic ------------------------

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("release.tar")]
    [InlineData("bundle.7z")]
    [InlineData("data.gz")]
    [InlineData("app.exe")]
    public void Non_byte_perfect_extension_with_entries_uses_semantic_reconstruction(string filename)
    {
        var result = DefaultPlanner().Plan(MakeInput(filename), SomeEntries());
        result.StoreOpaque.Should().BeFalse();
        result.ReconstructionKind.Should().Be(ReconstructionKind.Semantic);
    }
}
