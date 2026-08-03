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
