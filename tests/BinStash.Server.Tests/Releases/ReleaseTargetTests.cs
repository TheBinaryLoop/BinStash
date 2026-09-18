// Copyright (C) Lukas Eßmann — AGPLv3 or later

using BinStash.Contracts.Release;
using FluentAssertions;

namespace BinStash.Server.Tests.Releases;

/// <summary>
/// Targets have to be invisible to anyone who does not want them. A tenant publishing one payload
/// per release should never type the word, and everything published before targets existed must
/// keep working untouched.
/// </summary>
public class ReleaseTargetDefaultTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnspecifiedTarget_FoldsToTheDefault(string? input)
    {
        ReleaseTarget.TryCanonicalize(input, out var canonical, out var error).Should().BeTrue();

        error.Should().BeNull();
        canonical.Should().Be(ReleaseTarget.Default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    public void IsDefault_TreatsUnsetAndTheDefaultKeyAsTheSameThing(string? input)
    {
        // "no target" and "one unnamed target" have to be one case, not two, or every caller has
        // to branch on a distinction the user never made.
        ReleaseTarget.IsDefault(input).Should().BeTrue();
    }

    [Fact]
    public void IsDefault_IsFalseForARealTarget()
    {
        ReleaseTarget.IsDefault("linux-x64").Should().BeFalse();
    }
}

public class ReleaseTargetCanonicalizationTests
{
    [Theory]
    [InlineData("linux-x64", "linux-x64")]
    [InlineData("Linux-X64", "linux-x64")]
    [InlineData("  win-x64  ", "win-x64")]
    [InlineData("linux/arm64/v8", "linux/arm64/v8")]
    [InlineData("osx.arm64", "osx.arm64")]
    [InlineData("debug_cuda", "debug_cuda")]
    [InlineData(@"linux\arm64", "linux/arm64")]
    public void CanonicalizesToOneSpelling(string input, string expected)
    {
        ReleaseTarget.Canonicalize(input).Should().Be(expected);
    }

    [Fact]
    public void CaseVariantsAreTheSameTarget_NotTwo()
    {
        // This is what makes the unique index mean what a user expects: publishing Linux-x64
        // after linux-x64 is a duplicate, not a second target.
        ReleaseTarget.Canonicalize("Linux-x64").Should().Be(ReleaseTarget.Canonicalize("linux-x64"));
    }

    [Theory]
    [InlineData("linux x64")]      // spaces
    [InlineData("-linux")]         // leading separator
    [InlineData("linux-")]         // trailing separator
    [InlineData("linux--x64")]     // doubled separator
    [InlineData("linux@x64")]      // stray punctuation
    [InlineData("../etc/passwd")]  // path traversal: targets reach filenames and URLs
    public void RejectsAnythingThatWouldNotSurviveBeingPutInAPathOrUrl(string input)
    {
        ReleaseTarget.TryCanonicalize(input, out _, out var error).Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void RejectsAnOverlongTarget()
    {
        var tooLong = new string('a', ReleaseTarget.MaxLength + 1);

        ReleaseTarget.TryCanonicalize(tooLong, out _, out var error).Should().BeFalse();
        error.Should().Contain(ReleaseTarget.MaxLength.ToString());
    }

    [Fact]
    public void CanonicalizeThrowsWhereTryCanonicalizeReports()
    {
        var act = () => ReleaseTarget.Canonicalize("not a target");

        act.Should().Throw<ArgumentException>();
    }
}
