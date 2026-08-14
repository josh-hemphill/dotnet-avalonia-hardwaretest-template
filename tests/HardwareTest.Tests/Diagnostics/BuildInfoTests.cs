using System.Reflection;
using System.Text.RegularExpressions;
using HardwareTest.Core.Diagnostics;
using Xunit;

namespace HardwareTest.Tests.Diagnostics;

public sealed class BuildInfoTests
{
    [Fact]
    public void FromAssembly_populates_non_empty_version_fields()
    {
        var info = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.False(string.IsNullOrWhiteSpace(info.InformationalVersion));
        Assert.False(string.IsNullOrWhiteSpace(info.CommitSha));
        Assert.False(string.IsNullOrWhiteSpace(info.RuntimeVersion));
        Assert.False(string.IsNullOrWhiteSpace(info.RuntimeIdentifier));
        Assert.StartsWith("0.1.0", info.InformationalVersion, StringComparison.Ordinal);
        Assert.Contains('+', info.InformationalVersion);
        Assert.Null(info.OpenTapEngineVersion);
    }

    [Fact]
    public void InformationalVersion_is_deterministic_commit_metadata_without_wall_clock()
    {
        var info = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);
        Assert.False(
            Regex.IsMatch(info.InformationalVersion, @"\.\d{14}$"),
            $"InformationalVersion must not embed yyyyMMddHHmmss: {info.InformationalVersion}");

        BuildInfo.ParseInformational(info.InformationalVersion, out var parsedCommit, out var stampUtc);
        Assert.Equal(info.CommitSha, parsedCommit);
        Assert.Null(stampUtc);

        var commitDate = typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "CommitDate");
        // Git builds (CI and a normal checkout) must stamp CommitDate. "local" is the
        // no-git fallback and has no committer time — do not require it there.
        if (!string.Equals(info.CommitSha, "local", StringComparison.Ordinal))
        {
            Assert.False(string.IsNullOrWhiteSpace(commitDate?.Value),
                "CommitDate metadata missing; Windows cmd.exe likely ate git %cI");
            Assert.NotNull(info.BuildTimestampUtc);
            Assert.DoesNotContain("unknown", info.FormatSupportBlock(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("0.1.0+abc1234", "abc1234", false)]
    [InlineData("0.1.0+local", "local", false)]
    [InlineData("0.1.0+abc1234.20260728220000", "abc1234", true)]
    public void ParseInformational_accepts_current_and_legacy_stamps(
        string informational, string commit, bool hasStamp)
    {
        BuildInfo.ParseInformational(informational, out var parsed, out var stamp);
        Assert.Equal(commit, parsed);
        Assert.Equal(hasStamp, stamp.HasValue);
        if (hasStamp)
        {
            Assert.Equal(2026, stamp!.Value.UtcDateTime.Year);
            Assert.Equal(7, stamp.Value.UtcDateTime.Month);
            Assert.Equal(28, stamp.Value.UtcDateTime.Day);
        }
    }

    [Fact]
    public void FormatSupportBlock_includes_version_and_data_directory()
    {
        var info = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly)
            .WithOpenTapEngineVersion("9.9.9");
        var block = info.FormatSupportBlock(@"C:\data");

        Assert.Contains(info.InformationalVersion, block, StringComparison.Ordinal);
        Assert.Contains("OpenTAP: 9.9.9", block, StringComparison.Ordinal);
        Assert.Contains(@"DataDirectory: C:\data", block, StringComparison.Ordinal);
    }

    [Fact]
    public void WithOpenTapEngineVersion_preserves_other_fields()
    {
        var original = BuildInfo.FromAssembly(typeof(BuildInfo).Assembly);
        var attached = original.WithOpenTapEngineVersion("opentap-test");

        Assert.Equal(original.InformationalVersion, attached.InformationalVersion);
        Assert.Equal(original.CommitSha, attached.CommitSha);
        Assert.Equal("opentap-test", attached.OpenTapEngineVersion);
    }
}
