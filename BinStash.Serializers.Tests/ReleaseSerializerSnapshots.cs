using System.Reflection;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization;

namespace BinStash.Serializers.Tests;

public class ReleaseSerializerSnapshots
{
    [Fact]
    public async Task Release_format_is_stable()
    {
        // This test ensures that the serialization format of ReleasePackage remains stable.
        // If any changes are made to the serialization logic, this test will fail,
        // prompting a review of the changes to ensure backward compatibility.

        var releasePackage = await TestData.GetSampleReleasePackageAsync();
        var serializedData = await ReleasePackageSerializer.SerializeAsync(releasePackage);
        await Verify(serializedData);
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
        return releasePackage;
    }
}
