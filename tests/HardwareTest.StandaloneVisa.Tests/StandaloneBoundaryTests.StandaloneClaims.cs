using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    [Theory]
    [InlineData("nested-wrapper", false)]
    [InlineData("nested-wrapper", true)]
    [InlineData("nested-provider", false)]
    [InlineData("nested-provider", true)]
    [InlineData("nested-visa", false)]
    [InlineData("nested-visa", true)]
    [InlineData("case-wrapper", false)]
    [InlineData("case-wrapper", true)]
    [InlineData("case-provider", false)]
    [InlineData("case-provider", true)]
    [InlineData("case-visa", false)]
    [InlineData("case-visa", true)]
    [InlineData("metadata-nested", false)]
    [InlineData("metadata-nested", true)]
    [InlineData("metadata-file-case", false)]
    [InlineData("metadata-file-case", true)]
    [InlineData("metadata-directory-case", false)]
    [InlineData("metadata-directory-case", true)]
    [InlineData("metadata-name-case", false)]
    [InlineData("metadata-name-case", true)]
    [InlineData("metadata-name-padded", false)]
    [InlineData("metadata-name-padded", true)]
    public async Task Every_managed_root_rejects_noncanonical_standalone_claims_before_any_library_or_provider_load(string alias, bool reverseOrder)
    {
        if (OperatingSystem.IsWindows() && (alias.StartsWith("case-", StringComparison.Ordinal) || alias is "metadata-file-case" or "metadata-directory-case"))
            Assert.Skip("Distinct case-alias entries require a case-sensitive filesystem.");
        var first = InstalledHome(); var second = InstalledHome();
        Assert.True(StandaloneVisaReadiness.Assess(new(first)).Available);
        Assert.True(StandaloneVisaReadiness.Assess(new(second)).Available);
        var custom = Path.Combine(second, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        var metadata = Path.Combine(second, "Packages", StandaloneVisaPackage.PackageName, "package.xml");
        if (!alias.StartsWith("metadata-", StringComparison.Ordinal))
        {
            var file = alias.EndsWith("wrapper", StringComparison.Ordinal) ? StandaloneVisaPackage.WrapperFileName
                : alias.EndsWith("provider", StringComparison.Ordinal) ? "InstrumentComponents.OpenTap.Visa.dll" : "InstrumentComponents.Visa.dll";
            File.Copy(Path.Combine(second, file), alias.StartsWith("nested-", StringComparison.Ordinal)
                ? Path.Combine(custom, file) : Path.Combine(second, file.ToLowerInvariant()));
        }
        else if (alias is "metadata-name-case" or "metadata-name-padded")
        {
            var document = XDocument.Load(metadata);
            document.Root!.SetAttributeValue("Name", alias == "metadata-name-padded"
                ? " " + StandaloneVisaPackage.PackageName + " " : StandaloneVisaPackage.PackageName.ToLowerInvariant());
            document.Save(metadata);
        }
        else
        {
            var target = alias == "metadata-nested" ? Path.Combine(custom, "package.xml")
                : alias == "metadata-file-case" ? Path.Combine(Path.GetDirectoryName(metadata)!, "PACKAGE.XML")
                : Path.Combine(second, "Packages", StandaloneVisaPackage.PackageName.ToLowerInvariant(), "package.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(metadata, target);
        }
        var before = new[] { first, second }.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes);
        var roots = reverseOrder ? new[] { second, first } : new[] { first, second };
        var result = await Run(first, "HardwareTest.StandaloneVisa.ProcessFixture.dll", SerializeRoots(roots), "--invalid-multiple-roots", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("broker-binding-absent-on-cold-rejection", result.Output);
        Assert.Contains("canonical", result.Output);
        var after = new[] { first, second }.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public async Task Broker_execution_does_not_require_a_standalone_counterpart_when_no_counterpart_is_installed()
    {
        var home = InstalledHome();
        using (var stream = StandaloneVisaPackage.OpenArchive())
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            foreach (var entry in archive.Entries) File.Delete(Path.Combine(home, entry.FullName));
        Assert.False(File.Exists(Path.Combine(home, StandaloneVisaPackage.WrapperFileName)));
        Assert.False(File.Exists(Path.Combine(home, "Packages", StandaloneVisaPackage.PackageName, "package.xml")));
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home);
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
        Assert.Contains("published-instrument-opened-with-embedded-registry-and-closed", result.Output);
    }
}
