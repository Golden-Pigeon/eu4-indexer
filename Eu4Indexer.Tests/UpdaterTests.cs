using System.Runtime.InteropServices;
using Eu4Indexer.Core;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// Unit tests for the pure / file-local parts of self-update: version
/// comparison, RID mapping, and config-ref drift detection. The network and
/// self-replace paths are exercised manually (see docs/commands.md), mirroring
/// how Setup's download isn't unit-tested.
/// </summary>
public class UpdaterTests
{
    [Theory]
    [InlineData("v0.4.0", "0.3.0", true)]
    [InlineData("0.4.0", "0.3.0", true)]
    [InlineData("v0.3.0", "0.3.0", false)] // equal is not newer
    [InlineData("v0.2.9", "0.3.0", false)] // older
    [InlineData("v1.0.0", "0.9.9", true)]
    [InlineData("garbage", "0.3.0", false)] // unparseable -> not newer
    public void IsNewer_ComparesReleaseTags(string latest, string current, bool expected)
    {
        Assert.Equal(expected, Updater.isNewer(latest, current));
    }

    [Fact]
    public void ParseTag_StripsLeadingV()
    {
        Assert.True(FSharpOption<Version>.get_IsSome(Updater.parseTag("v1.2.3")));
        Assert.Equal(new Version(1, 2, 3), Updater.parseTag("v1.2.3").Value);
        Assert.True(FSharpOption<Version>.get_IsNone(Updater.parseTag("not-a-version")));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public void RidFor_MapsKnownTargets(string expected)
    {
        var os = expected.StartsWith("win-") ? Updater.OsKind.Windows
            : expected.StartsWith("osx-") ? Updater.OsKind.MacOS
            : Updater.OsKind.Linux;
        var arch = expected.EndsWith("-arm64") ? Architecture.Arm64 : Architecture.X64;

        var result = Updater.ridFor(os, arch);

        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        Assert.Equal(expected, result.ResultValue);
    }

    [Fact]
    public void RidFor_RejectsUnsupportedArch()
    {
        Assert.True(Updater.ridFor(Updater.OsKind.Linux, Architecture.X86).IsError);
    }

    [Fact]
    public void InstalledRef_RoundTripsAndDetectsDrift()
    {
        var home = Path.Combine(Path.GetTempPath(), "eu4indexer-test-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("EU4INDEXER_HOME");

        try
        {
            Environment.SetEnvironmentVariable("EU4INDEXER_HOME", home);
            var configEu4 = Path.Combine(home, "config", "eu4");
            Directory.CreateDirectory(configEu4);

            var pinned = Setup.configForGame("eu4").Value.Ref;

            // No marker yet (legacy install): readable as None, treated as stale.
            Assert.True(FSharpOption<string>.get_IsNone(Setup.installedRef("eu4")));
            Assert.True(Setup.isStale("eu4"));

            // Marker matching the pinned ref: not stale, round-trips.
            File.WriteAllText(Path.Combine(configEu4, ".eu4indexer-ref"), pinned);
            Assert.Equal(FSharpOption<string>.Some(pinned), Setup.installedRef("eu4"));
            Assert.False(Setup.isStale("eu4"));

            // Marker with an old ref: stale.
            File.WriteAllText(Path.Combine(configEu4, ".eu4indexer-ref"), "0000000000000000000000000000000000000000");
            Assert.True(Setup.isStale("eu4"));

            // A game whose config was never installed is not stale.
            Assert.False(Setup.isStale("hoi4"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EU4INDEXER_HOME", previous);
            try { Directory.Delete(home, true); } catch { /* best effort */ }
        }
    }
}
