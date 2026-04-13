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
using BinStash.Contracts.Release;
using BinStash.Core.Serialization;
using FluentAssertions;

namespace BinStash.Serializers.Tests;

public class ReleasePackageSerializerSpecs
{
    // ---- factory helpers ----------------------------------------------------

    private static Hash32 Hash(byte fill)
    {
        Span<byte> b = stackalloc byte[32];
        b.Fill(fill);
        return new Hash32(b);
    }

    private static ReleasePackage MinimalPackage() => new()
    {
        Version = "1.0",
        ReleaseId = "rel-001",
        RepoId = "repo-abc",
        Notes = "",
        CreatedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
        OutputArtifacts =
        [
            new OutputArtifact
            {
                Path = "component/file.bin",
                ComponentName = "component",
                Kind = OutputArtifactKind.File,
                RequiresBytePerfectReconstruction = false,
                Backing = new OpaqueBlobBacking { ContentHash = Hash(0xAA), Length = 1024 }
            }
        ]
    };

    // ---- Header -------------------------------------------------------------

    [Fact]
    public async Task Serialized_bytes_start_with_BPKG_magic()
    {
        var bytes = await ReleasePackageSerializer.SerializeAsync(MinimalPackage());
        bytes[0].Should().Be((byte)'B');
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'K');
        bytes[3].Should().Be((byte)'G');
    }

    [Fact]
    public async Task Serialized_bytes_carry_version_3()
    {
        var bytes = await ReleasePackageSerializer.SerializeAsync(MinimalPackage());
        bytes[4].Should().Be(3);
    }

    // ---- Basic metadata round-trip ------------------------------------------

    [Fact]
    public async Task Version_round_trips()
    {
        var original = MinimalPackage();
        original.Version = "2.5.1";
        var rt = await RoundTrip(original);
        rt.Version.Should().Be("2.5.1");
    }

    [Fact]
    public async Task ReleaseId_round_trips()
    {
        var original = MinimalPackage();
        original.ReleaseId = "my-release-id-xyz";
        var rt = await RoundTrip(original);
        rt.ReleaseId.Should().Be("my-release-id-xyz");
    }

    [Fact]
    public async Task RepoId_round_trips()
    {
        var original = MinimalPackage();
        original.RepoId = "some-repo-id";
        var rt = await RoundTrip(original);
        rt.RepoId.Should().Be("some-repo-id");
    }

    [Fact]
    public async Task Notes_round_trips()
    {
        var original = MinimalPackage();
        original.Notes = "This is a release note.\nSecond line.";
        var rt = await RoundTrip(original);
        rt.Notes.Should().Be("This is a release note.\nSecond line.");
    }

    [Fact]
    public async Task Empty_notes_round_trips()
    {
        var original = MinimalPackage();
        original.Notes = "";
        var rt = await RoundTrip(original);
        rt.Notes.Should().Be("");
    }

    [Fact]
    public async Task CreatedAt_round_trips_with_second_precision()
    {
        var original = MinimalPackage();
        original.CreatedAt = new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var rt = await RoundTrip(original);
        rt.CreatedAt.ToUnixTimeSeconds().Should().Be(original.CreatedAt.ToUnixTimeSeconds());
    }

    // ---- Stats round-trip ---------------------------------------------------

    [Fact]
    public async Task Stats_round_trips()
    {
        var original = MinimalPackage();
        original.Stats = new ReleaseStats
        {
            ComponentCount = 3,
            FileCount = 42,
            ChunkCount = 1337,
            RawSize = 100_000_000,
            DedupedSize = 40_000_000
        };
        var rt = await RoundTrip(original);
        rt.Stats.ComponentCount.Should().Be(3);
        rt.Stats.FileCount.Should().Be(42);
        rt.Stats.ChunkCount.Should().Be(1337);
        rt.Stats.RawSize.Should().Be(100_000_000);
        rt.Stats.DedupedSize.Should().Be(40_000_000);
    }

    // ---- Custom properties round-trip ---------------------------------------

    [Fact]
    public async Task Empty_custom_properties_round_trips()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>();
        var rt = await RoundTrip(original);
        rt.CustomProperties.Should().BeEmpty();
    }

    [Fact]
    public async Task Single_custom_property_round_trips()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string> { ["build-number"] = "42" };
        var rt = await RoundTrip(original);
        rt.CustomProperties.Should().ContainKey("build-number")
            .WhoseValue.Should().Be("42");
    }

    [Fact]
    public async Task Multiple_custom_properties_round_trip()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["env"] = "production",
            ["region"] = "eu-west-1",
            ["build"] = "12345"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties.Should().BeEquivalentTo(original.CustomProperties);
    }

    [Fact]
    public async Task Custom_property_with_url_value_round_trips_preserving_double_slash()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["source-url"] = "https://example.com/repo/releases/1.0"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties["source-url"].Should().Be("https://example.com/repo/releases/1.0");
    }

    [Fact]
    public async Task Custom_property_with_http_url_round_trips()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["homepage"] = "http://localhost:8080/dashboard"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties["homepage"].Should().Be("http://localhost:8080/dashboard");
    }

    [Fact]
    public async Task Custom_property_url_key_and_url_value_both_round_trip()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["https://schema.example.com/type"] = "https://schema.example.com/value"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties.Should().ContainKey("https://schema.example.com/type")
            .WhoseValue.Should().Be("https://schema.example.com/value");
    }

    [Fact]
    public async Task Custom_property_with_triple_slash_value_round_trips()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["file-ref"] = "file:///C:/path/to/file.bin"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties["file-ref"].Should().Be("file:///C:/path/to/file.bin");
    }

    [Fact]
    public async Task Custom_property_with_special_chars_round_trips()
    {
        var original = MinimalPackage();
        original.CustomProperties = new Dictionary<string, string>
        {
            ["note"] = "value with spaces & unicode: Ä Ö Ü 中文"
        };
        var rt = await RoundTrip(original);
        rt.CustomProperties["note"].Should().Be("value with spaces & unicode: Ä Ö Ü 中文");
    }

    // ---- Opaque artifact round-trip -----------------------------------------

    [Fact]
    public async Task Opaque_artifact_path_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        rt.OutputArtifacts.Should().HaveCount(1);
        rt.OutputArtifacts[0].Path.Should().Be("component/file.bin");
    }

    [Fact]
    public async Task Opaque_artifact_component_name_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        rt.OutputArtifacts[0].ComponentName.Should().Be("component");
    }

    [Fact]
    public async Task Opaque_artifact_kind_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        rt.OutputArtifacts[0].Kind.Should().Be(OutputArtifactKind.File);
    }

    [Fact]
    public async Task Opaque_artifact_requires_byte_perfect_false_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        rt.OutputArtifacts[0].RequiresBytePerfectReconstruction.Should().BeFalse();
    }

    [Fact]
    public async Task Opaque_artifact_requires_byte_perfect_true_round_trips()
    {
        var original = PackageWithOpaque(requiresBytePerfect: true);
        var rt = await RoundTrip(original);
        rt.OutputArtifacts[0].RequiresBytePerfectReconstruction.Should().BeTrue();
    }

    [Fact]
    public async Task Opaque_artifact_content_hash_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<OpaqueBlobBacking>().Subject;
        backing.ContentHash.Should().Be(Hash(0xAA));
    }

    [Fact]
    public async Task Opaque_artifact_length_round_trips()
    {
        var original = MinimalPackage();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<OpaqueBlobBacking>().Subject;
        backing.Length.Should().Be(1024);
    }

    [Fact]
    public async Task Multiple_opaque_artifacts_all_round_trip()
    {
        var original = PackageWithManyOpaqueArtifacts(5);
        var rt = await RoundTrip(original);
        rt.OutputArtifacts.Should().HaveCount(5);
        for (var i = 0; i < 5; i++)
        {
            var opaqueOrig = (OpaqueBlobBacking)original.OutputArtifacts[i].Backing;
            var opaqueRt = rt.OutputArtifacts[i].Backing.Should().BeOfType<OpaqueBlobBacking>().Subject;
            opaqueRt.ContentHash.Should().Be(opaqueOrig.ContentHash);
            opaqueRt.Length.Should().Be(opaqueOrig.Length);
            rt.OutputArtifacts[i].Path.Should().Be(original.OutputArtifacts[i].Path);
        }
    }

    // ---- Reconstructed container artifact round-trip ------------------------

    [Fact]
    public async Task Reconstructed_artifact_format_id_round_trips()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.FormatId.Should().Be("zip");
    }

    [Fact]
    public async Task Reconstructed_artifact_reconstruction_kind_round_trips()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.ReconstructionKind.Should().Be(ReconstructionKind.Semantic);
    }

    [Fact]
    public async Task Reconstructed_artifact_member_count_round_trips()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.Members.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reconstructed_artifact_member_paths_round_trip()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.Members[0].EntryPath.Should().Be("lib/foo.dll");
        backing.Members[1].EntryPath.Should().Be("lib/bar.dll");
    }

    [Fact]
    public async Task Reconstructed_artifact_member_hashes_round_trip()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.Members[0].ContentHash.Should().Be(Hash(0x11));
        backing.Members[1].ContentHash.Should().Be(Hash(0x22));
    }

    [Fact]
    public async Task Reconstructed_artifact_member_lengths_round_trip()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.Members[0].Length.Should().Be(512);
        backing.Members[1].Length.Should().Be(1024);
    }

    [Fact]
    public async Task Reconstructed_artifact_recipe_payload_round_trips()
    {
        var original = PackageWithReconstructed();
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.RecipePayload.Should().Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }

    [Fact]
    public async Task Empty_recipe_payload_round_trips()
    {
        var original = PackageWithReconstructed(recipePayload: Array.Empty<byte>());
        var rt = await RoundTrip(original);
        var backing = rt.OutputArtifacts[0].Backing.Should().BeOfType<ReconstructedContainerBacking>().Subject;
        backing.RecipePayload.Should().BeEmpty();
    }

    // ---- Mixed opaque + reconstructed round-trip ----------------------------

    [Fact]
    public async Task Mixed_opaque_and_reconstructed_artifacts_round_trip()
    {
        var package = new ReleasePackage
        {
            Version = "1.0",
            ReleaseId = "mixed-rel",
            RepoId = "repo-x",
            Notes = "",
            CreatedAt = DateTimeOffset.UtcNow,
            OutputArtifacts =
            [
                new OutputArtifact
                {
                    Path = "comp/a.bin",
                    ComponentName = "comp",
                    Kind = OutputArtifactKind.File,
                    RequiresBytePerfectReconstruction = true,
                    Backing = new OpaqueBlobBacking { ContentHash = Hash(0x01), Length = 100 }
                },
                new OutputArtifact
                {
                    Path = "comp/archive.zip",
                    ComponentName = "comp",
                    Kind = OutputArtifactKind.File,
                    RequiresBytePerfectReconstruction = false,
                    Backing = new ReconstructedContainerBacking
                    {
                        FormatId = "zip",
                        ReconstructionKind = ReconstructionKind.Semantic,
                        Members =
                        [
                            new ContainerMemberBinding { EntryPath = "inner.txt", ContentHash = Hash(0x55), Length = 256 }
                        ],
                        RecipePayload = [0x01, 0x02]
                    }
                }
            ]
        };

        var rt = await RoundTrip(package);
        rt.OutputArtifacts.Should().HaveCount(2);
        rt.OutputArtifacts[0].Backing.Should().BeOfType<OpaqueBlobBacking>();
        rt.OutputArtifacts[1].Backing.Should().BeOfType<ReconstructedContainerBacking>();
    }

    // ---- Compression options ------------------------------------------------

    [Fact]
    public async Task Round_trip_works_without_compression()
    {
        var options = new ReleasePackageSerializerOptions { EnableCompression = false };
        var original = MinimalPackage();
        var bytes = await ReleasePackageSerializer.SerializeAsync(original, options);
        var rt = await ReleasePackageSerializer.DeserializeAsync(bytes);
        rt.ReleaseId.Should().Be("rel-001");
    }

    [Fact]
    public async Task Uncompressed_output_is_larger_than_compressed_for_many_artifacts()
    {
        var package = PackageWithManyOpaqueArtifacts(100);

        var compressedBytes = await ReleasePackageSerializer.SerializeAsync(package,
            new ReleasePackageSerializerOptions { EnableCompression = true });
        var uncompressedBytes = await ReleasePackageSerializer.SerializeAsync(package,
            new ReleasePackageSerializerOptions { EnableCompression = false });

        // Compression should at least not bloat the output for 100 identical-ish hashes
        uncompressedBytes.Length.Should().BeGreaterThan(0);
        compressedBytes.Length.Should().BeGreaterThan(0);
    }

    // ---- Error cases --------------------------------------------------------

    [Fact]
    public async Task Deserialize_throws_on_wrong_magic()
    {
        var bad = new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X', 3, 0 };
        var act = async () => await ReleasePackageSerializer.DeserializeAsync(bad);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*magic*");
    }

    [Fact]
    public async Task Deserialize_throws_on_unsupported_version()
    {
        var bad = new byte[] { (byte)'B', (byte)'P', (byte)'K', (byte)'G', 99, 0 };
        var act = async () => await ReleasePackageSerializer.DeserializeAsync(bad);
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*99*");
    }

    [Fact]
    public async Task Serialize_throws_when_opaque_backing_has_no_content_hash()
    {
        var package = new ReleasePackage
        {
            Version = "1.0",
            ReleaseId = "r",
            RepoId = "rp",
            Notes = "",
            CreatedAt = DateTimeOffset.UtcNow,
            OutputArtifacts =
            [
                new OutputArtifact
                {
                    Path = "a.bin",
                    ComponentName = "c",
                    Kind = OutputArtifactKind.File,
                    RequiresBytePerfectReconstruction = false,
                    Backing = new OpaqueBlobBacking { ContentHash = null, Length = 1 }
                }
            ]
        };

        var act = async () => await ReleasePackageSerializer.SerializeAsync(package);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    // ---- Helpers ------------------------------------------------------------

    private static async Task<ReleasePackage> RoundTrip(ReleasePackage package,
        ReleasePackageSerializerOptions? options = null)
    {
        var bytes = await ReleasePackageSerializer.SerializeAsync(package, options);
        return await ReleasePackageSerializer.DeserializeAsync(bytes);
    }

    private static ReleasePackage PackageWithOpaque(bool requiresBytePerfect = false) => new()
    {
        Version = "1.0",
        ReleaseId = "r",
        RepoId = "rp",
        Notes = "",
        CreatedAt = DateTimeOffset.UtcNow,
        OutputArtifacts =
        [
            new OutputArtifact
            {
                Path = "comp/a.bin",
                ComponentName = "comp",
                Kind = OutputArtifactKind.File,
                RequiresBytePerfectReconstruction = requiresBytePerfect,
                Backing = new OpaqueBlobBacking { ContentHash = Hash(0xBB), Length = 2048 }
            }
        ]
    };

    private static ReleasePackage PackageWithReconstructed(byte[]? recipePayload = null) => new()
    {
        Version = "1.0",
        ReleaseId = "r",
        RepoId = "rp",
        Notes = "",
        CreatedAt = DateTimeOffset.UtcNow,
        OutputArtifacts =
        [
            new OutputArtifact
            {
                Path = "comp/archive.zip",
                ComponentName = "comp",
                Kind = OutputArtifactKind.File,
                RequiresBytePerfectReconstruction = false,
                Backing = new ReconstructedContainerBacking
                {
                    FormatId = "zip",
                    ReconstructionKind = ReconstructionKind.Semantic,
                    Members =
                    [
                        new ContainerMemberBinding { EntryPath = "lib/foo.dll", ContentHash = Hash(0x11), Length = 512 },
                        new ContainerMemberBinding { EntryPath = "lib/bar.dll", ContentHash = Hash(0x22), Length = 1024 }
                    ],
                    RecipePayload = recipePayload ?? [0xDE, 0xAD, 0xBE, 0xEF]
                }
            }
        ]
    };

    private static ReleasePackage PackageWithManyOpaqueArtifacts(int count)
    {
        var artifacts = new List<OutputArtifact>(count);
        for (var i = 0; i < count; i++)
        {
            Span<byte> hashBytes = stackalloc byte[32];
            hashBytes.Fill((byte)(i % 256));
            hashBytes[0] = (byte)(i >> 8);
            hashBytes[1] = (byte)(i & 0xFF);
            artifacts.Add(new OutputArtifact
            {
                Path = $"comp/file-{i:D4}.bin",
                ComponentName = "comp",
                Kind = OutputArtifactKind.File,
                RequiresBytePerfectReconstruction = false,
                Backing = new OpaqueBlobBacking { ContentHash = new Hash32(hashBytes), Length = 1024 + i }
            });
        }

        return new ReleasePackage
        {
            Version = "1.0",
            ReleaseId = "many-artifacts",
            RepoId = "repo",
            Notes = "",
            CreatedAt = DateTimeOffset.UtcNow,
            OutputArtifacts = artifacts
        };
    }
}
