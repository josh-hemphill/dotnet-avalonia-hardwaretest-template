using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    [Theory]
    [InlineData("outside-link", false)]
    [InlineData("outside-link", true)]
    [InlineData("nested-dll", false)]
    [InlineData("nested-dll", true)]
    [InlineData("duplicate-metadata", false)]
    [InlineData("duplicate-metadata", true)]
    [InlineData("stale-hash", false)]
    [InlineData("stale-hash", true)]
    [InlineData("malformed-hash", false)]
    [InlineData("malformed-hash", true)]
    [InlineData("wrong-version", false)]
    [InlineData("wrong-version", true)]
    [InlineData("conflicting-current", false)]
    [InlineData("conflicting-current", true)]
    public async Task Every_configured_execution_root_is_validated_before_library_loading_or_plugin_search(string defect, bool reverseOrder)
    {
        if (defect == "outside-link" && OperatingSystem.IsWindows()) return;
        var first = InstalledHome();
        var second = InstalledHome();
        var plugins = Path.Combine(second, "Plugins", "Custom");
        Directory.CreateDirectory(plugins);
        var provider = Path.Combine(second, "InstrumentComponents.OpenTap.dll");
        var metadata = Path.Combine(second, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        var hash = document.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == "InstrumentComponents.OpenTap.dll")
            .Elements().Single(element => element.Name.LocalName == "Hash");
        string? link = null;
        string? outside = null;
        switch (defect)
        {
            case "outside-link":
                outside = Path.Combine(_root, "outside.txt");
                File.WriteAllText(outside, "outside-second-root");
                link = Path.Combine(plugins, "external.txt");
                File.CreateSymbolicLink(link, outside);
                break;
            case "nested-dll": File.Copy(provider, Path.Combine(plugins, "InstrumentComponents.OpenTap.dll")); break;
            case "duplicate-metadata": document.Save(Path.Combine(plugins, "package.xml")); break;
            case "malformed-hash": hash.Value = "malformed-sha1"; document.Save(metadata); break;
            default:
                var bytes = File.ReadAllBytes(provider);
                if (defect == "wrong-version")
                {
                    int versionOffset;
                    using (var stream = new MemoryStream(bytes, writable: false))
                    using (var pe = new PEReader(stream))
                        versionOffset = pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.Assembly) + sizeof(uint);
                    bytes[versionOffset + 4] = 0;
                    bytes[versionOffset + 5] = 0;
                }
                else
                    bytes = [.. bytes, 1];
                File.WriteAllBytes(provider, bytes);
                if (defect != "stale-hash")
                {
                    hash.Value = Convert.ToHexString(SHA1.HashData(bytes));
                    document.Save(metadata);
                }
                break;
        }
        var before = new[] { first, second }.SelectMany(home => Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes);
        var roots = reverseOrder ? new[] { second, first } : new[] { first, second };
        var result = await Run(first, "HardwareTest.StandaloneVisa.ProcessFixture.dll", SerializeRoots(roots), "--invalid-multiple-roots", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("broker-binding-absent-on-cold-rejection", result.Output);
        Assert.Contains(defect switch
        {
            "outside-link" => "resolves outside its root",
            "nested-dll" => "cannot use obsolete package-directory library DLLs",
            "duplicate-metadata" => "noncanonical or duplicate installed package identities",
            "stale-hash" => "does not match its package metadata hash",
            "malformed-hash" => "Malformed library payload hash",
            "wrong-version" => "requires current 0.1.1 library assemblies",
            _ => "Configured execution roots contain different Instrument Components payloads"
        }, result.Output);
        var after = new[] { first, second }.SelectMany(home => Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        if (link is not null)
        {
            Assert.Equal(outside, new FileInfo(link).LinkTarget);
            Assert.Equal("outside-second-root", File.ReadAllText(outside!));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsafe_root_without_a_library_claim_cannot_grant_fallback_even_after_a_safe_root(bool reverseOrder)
    {
        var processHome = InstalledHome();
        var first = Path.Combine(_root, "safe-plugins");
        var second = Path.Combine(_root, "unsafe-plugins");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(Path.Combine(second, "Plugins", "Alias"));
        File.WriteAllText(Path.Combine(first, "notes.txt"), "contained-plugin-data");
        var metadata = Path.Combine(second, "Plugins", "Alias", "package.xml");
        File.WriteAllText(metadata, "<Package Name=\"InstrumentComponents.OpenTap\" Version=\"0.1.1\"/>");
        var original = File.ReadAllBytes(metadata);
        var roots = reverseOrder ? new[] { second, first } : new[] { first, second };
        var result = await Run(processHome, "HardwareTest.StandaloneVisa.ProcessFixture.dll", SerializeRoots(roots), "--invalid-multiple-roots", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("broker-binding-absent-on-cold-rejection", result.Output);
        Assert.Contains("noncanonical or duplicate installed package identities", result.Output);
        Assert.Equal(original, File.ReadAllBytes(metadata));
        Assert.Equal("contained-plugin-data", File.ReadAllText(Path.Combine(first, "notes.txt")));
    }

    [Theory]
    [InlineData("identical-libraries", false)]
    [InlineData("identical-libraries", true)]
    [InlineData("unrelated-plugins", false)]
    [InlineData("unrelated-plugins", true)]
    [InlineData("fallback-plugins", false)]
    [InlineData("fallback-plugins", true)]
    public async Task Multiple_safe_roots_accept_identical_libraries_and_unrelated_contained_plugins(string mode, bool reverseOrder)
    {
        var processHome = InstalledHome();
        var first = mode == "fallback-plugins" ? Path.Combine(_root, "first-plugins") : processHome;
        var second = mode == "identical-libraries" ? InstalledHome() : Path.Combine(_root, "second-plugins");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(Path.Combine(second, "Plugins", "Custom"));
        if (mode != "identical-libraries")
        {
            File.WriteAllText(Path.Combine(second, "Plugins", "Custom", "notes.txt"), "safe-plugin-data");
            File.WriteAllText(Path.Combine(second, "Plugins", "Custom", "package.xml"), "<Package Name=\"UnrelatedPlugin\" Version=\"1.0.0\"/>");
        }
        var roots = reverseOrder ? new[] { second, first } : new[] { first, second };
        var result = await Run(processHome, "HardwareTest.StandaloneVisa.ProcessFixture.dll", SerializeRoots(roots), "--managed-multiple-roots");
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
    }
    private static string SerializeRoots(IEnumerable<string> roots)
    {
        var array = new JsonArray();
        foreach (var root in roots) array.Add(JsonValue.Create(root));
        return array.ToJsonString();
    }

}
