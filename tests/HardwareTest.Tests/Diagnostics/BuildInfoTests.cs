using System.Reflection;
using System.Reflection.Emit;
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
        Assert.True(
            Regex.IsMatch(info.InformationalVersion, @"^0\.1\.0\+(?:[0-9a-f]+|local)$"),
            $"InformationalVersion must contain only version+commit: {info.InformationalVersion}");

        BuildInfo.ParseInformational(info.InformationalVersion, out var parsedCommit);
        Assert.Equal(info.CommitSha, parsedCommit);

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
    [InlineData("0.1.0+abc1234", "abc1234")]
    [InlineData("0.1.0+local", "local")]
    [InlineData("0.1.0", "local")]
    [InlineData("0.1.0+", "local")]
    public void ParseInformational_reads_deterministic_commit_metadata(
        string informational, string commit)
    {
        BuildInfo.ParseInformational(informational, out var parsed);
        Assert.Equal(commit, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("invalid-date")]
    public void Missing_or_invalid_CommitDate_leaves_build_timestamp_unknown(string? commitDate)
    {
        var assembly = BuildAssembly("0.1.0+abc1234", commitDate);

        var info = BuildInfo.FromAssembly(assembly);

        Assert.Equal("abc1234", info.CommitSha);
        Assert.Null(info.BuildTimestampUtc);
        Assert.Contains("BuildTimestampUtc: unknown", info.FormatSupportBlock(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_informational_date_suffix_cannot_supply_a_build_timestamp()
    {
        var assembly = BuildAssembly("0.1.0+abc1234.20260728220000", commitDate: null);

        var info = BuildInfo.FromAssembly(assembly);

        Assert.Null(info.BuildTimestampUtc);
        Assert.Contains("BuildTimestampUtc: unknown", info.FormatSupportBlock(), StringComparison.Ordinal);
    }

    [Fact]
    public void CommitDate_is_the_build_timestamp_and_is_normalized_to_utc()
    {
        var assembly = BuildAssembly("0.1.0+abc1234", "2026-09-10T12:00:00+02:00");

        var info = BuildInfo.FromAssembly(assembly);

        Assert.Equal(new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero), info.BuildTimestampUtc);
        Assert.Equal(TimeSpan.Zero, info.BuildTimestampUtc!.Value.Offset);
    }

    private static Assembly BuildAssembly(string informational, string? commitDate)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"BuildInfoFixture_{Guid.NewGuid():N}"), AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
            [informational]));
        if (commitDate is not null)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["CommitDate", commitDate]));
        }

        return assembly;
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
        Assert.Equal(original.BuildTimestampUtc, attached.BuildTimestampUtc);
        Assert.Equal("opentap-test", attached.OpenTapEngineVersion);
    }
}
