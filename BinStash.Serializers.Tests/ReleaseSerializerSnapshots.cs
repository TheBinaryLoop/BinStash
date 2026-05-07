using System.Reflection;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization;
using FluentAssertions;

namespace BinStash.Serializers.Tests;

public class ReleaseSerializerSnapshots
{
/*    [Fact]
    public async Task ReleasePackage_roundtrips_without_semantic_changes()
    {
        var original = await TestData.GetSampleReleasePackageAsync();

        var bytes = await ReleasePackageSerializer.SerializeAsync(original);
        var roundTripped = await ReleasePackageSerializer.DeserializeAsync(bytes);

        roundTripped.Should().BeEquivalentTo(original);
    }*/

    [Fact]
    public async Task Serializer_writes_current_format_version()
    {
        var package = await TestData.GetSampleReleasePackageAsync();

        var bytes = await ReleasePackageSerializer.SerializeAsync(package);

        bytes[0].Should().Be((byte)'B');
        bytes[1].Should().Be((byte)'P');
        bytes[2].Should().Be((byte)'K');
        bytes[3].Should().Be((byte)'G');
        bytes[4].Should().Be(ReleasePackageSerializer.Version);
    }
}

internal static class TestData
{
    public static async Task<ReleasePackage> GetSampleReleasePackageAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().First(x =>
            x.EndsWith("30af241bade9f2d1ca1ceb7d1311dc18de39bab337dcc54372f032586d120ae9.rdef"));
        await using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var releasePackage = await ReleasePackageSerializer.DeserializeAsync(bytes);
        foreach (var outputArtifact in releasePackage.OutputArtifacts.Where(x => x.Backing is OpaqueBlobBacking { Length: null }).Select(x => (OpaqueBlobBacking)x.Backing))
        {
            outputArtifact.Length = 0;
        }
        return releasePackage;
    }
}
