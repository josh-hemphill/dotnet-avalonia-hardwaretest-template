using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    [Theory]
    [InlineData("malformed", false)]
    [InlineData("malformed", true)]
    [InlineData("dtd", false)]
    [InlineData("dtd", true)]
    [InlineData("oversized", false)]
    [InlineData("oversized", true)]
    public async Task Uninspectable_nested_metadata_cannot_grant_managed_fallback_or_mutate_execution_state(string defect, bool canonicalLibrary)
    {
        var home = InstalledHome();
        if (!canonicalLibrary)
        {
            RemoveBaseFromFixture(home);
            File.Delete(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        }
        var plugins = Path.Combine(home, "Plugins", "Custom"); Directory.CreateDirectory(plugins);
        var content = defect switch
        {
            "dtd" => "<!DOCTYPE Package [<!ENTITY name 'InstrumentComponents.OpenTap'>]><Package Name=\"&name;\"/>",
            "oversized" => "<Package Name=\"InstrumentComponents.OpenTap\"><Description>" + new string('x', 1_048_577) + "</Description></Package>",
            _ => "<Package Name=\"InstrumentComponents.OpenTap\" Version=\"0.1.1\"><Files>",
        };
        File.WriteAllText(Path.Combine(plugins, "package.xml"), content);
        var before = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--invalid-selected-metadata", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("broker-binding-absent-on-cold-rejection", result.Output);
        Assert.Contains("package metadata is malformed or exceeds the inspection limit", result.Output);
        var after = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home, path), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
