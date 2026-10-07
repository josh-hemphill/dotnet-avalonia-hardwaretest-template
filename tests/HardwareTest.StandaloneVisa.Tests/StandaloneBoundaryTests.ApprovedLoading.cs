using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    [Theory]
    [InlineData("--replace-approved-contract")]
    [InlineData("--replace-approved-provider")]
    public async Task Replaced_stage_cannot_enter_the_CLR_after_metadata_and_managed_identity_approve_captured_bytes(string mode)
    {
        var home = InstalledHome();
        var before = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, mode, allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("approved-buffer-stage-replacement-refused-before-clr-load", result.Output);
        var after = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
