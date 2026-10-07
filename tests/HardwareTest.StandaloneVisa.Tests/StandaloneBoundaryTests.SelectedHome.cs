using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.StandaloneVisa.Tests;

public sealed partial class StandaloneBoundaryTests
{
    [Theory]
    [InlineData("older-provider")]
    [InlineData("wrong-provider-name")]
    [InlineData("malformed-provider")]
    public async Task Both_matching_hash_declared_library_identities_are_checked_before_either_assembly_loads(string defect)
    {
        var home = InstalledHome();
        var path = Path.Combine(home, "InstrumentComponents.OpenTap.dll");
        var bytes = File.ReadAllBytes(path);
        if (defect == "older-provider")
        {
            int versionOffset;
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var pe = new PEReader(stream))
                versionOffset = pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.Assembly) + sizeof(uint);
            bytes[versionOffset + 4] = 0;
            bytes[versionOffset + 5] = 0;
        }
        else if (defect == "wrong-provider-name")
            bytes = File.ReadAllBytes(Path.Combine(home, "InstrumentComponents.dll"));
        else
            bytes = "malformed-managed-provider"u8.ToArray();
        File.WriteAllBytes(path, bytes);
        var metadata = Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        var document = XDocument.Load(metadata);
        document.Descendants().Single(element => element.Name.LocalName == "File" && (string?)element.Attribute("Path") == "InstrumentComponents.OpenTap.dll")
            .Elements().Single(element => element.Name.LocalName == "Hash").Value = Convert.ToHexString(SHA1.HashData(bytes));
        document.Save(metadata);
        var before = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(home, file), File.ReadAllBytes);
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--invalid-selected-metadata", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains("requires current 0.1.1 library assemblies", result.Output);
        var after = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(home, file), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (file, original) in before) Assert.Equal(original, after[file]);
    }

    [Theory]
    [InlineData("nested-dll", false)]
    [InlineData("nested-dll", true)]
    [InlineData("alias-metadata", false)]
    [InlineData("alias-metadata", true)]
    [InlineData("outside-directory", false)]
    [InlineData("outside-directory", true)]
    [InlineData("outside-plugin-file", false)]
    [InlineData("outside-plugin-file", true)]
    [InlineData("outside-root-file", false)]
    [InlineData("outside-root-file", true)]
    [InlineData("cycle", false)]
    [InlineData("cycle", true)]
    public async Task Every_selected_root_child_is_validated_before_library_loading_or_bundled_fallback(string defect, bool canonicalLibrary)
    {
        var linked = defect is "outside-directory" or "outside-plugin-file" or "outside-root-file" or "cycle";
        if (linked && OperatingSystem.IsWindows()) return;
        var home = InstalledHome();
        var plugins = Path.Combine(home, "Plugins", "Custom");
        Directory.CreateDirectory(plugins);
        if (defect == "nested-dll")
            File.Copy(Path.Combine(home, "InstrumentComponents.OpenTap.dll"), Path.Combine(plugins, "InstrumentComponents.OpenTap.dll"));
        if (defect == "alias-metadata")
            File.WriteAllText(Path.Combine(plugins, "package.xml"), "<Package Name=\"InstrumentComponents.OpenTap\" Version=\"0.1.1\"/>");
        if (!canonicalLibrary)
        {
            RemoveBaseFromFixture(home);
            File.Delete(Path.Combine(home, "Packages", PublishedInstrumentComponents.PackageName, "package.xml"));
        }
        // Capture ordinary payload before adding a cycle; recursive fixture enumeration
        // must not follow the intentionally unsafe link tree.
        var before = Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(home, file), File.ReadAllBytes);
        string? linkPath = null;
        string? target = null;
        if (linked)
        {
            var outside = Path.Combine(_root, "outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            if (defect == "cycle")
            {
                linkPath = Path.Combine(plugins, "Cycle");
                target = home;
                Directory.CreateSymbolicLink(linkPath, target);
            }
            else if (defect == "outside-directory")
            {
                linkPath = Path.Combine(home, "Plugins", "External");
                target = outside;
                Directory.CreateSymbolicLink(linkPath, target);
            }
            else
            {
                target = Path.Combine(outside, "external.txt");
                File.WriteAllText(target, "outside-selected-home");
                linkPath = defect == "outside-root-file" ? Path.Combine(home, "external.txt") : Path.Combine(plugins, "external.txt");
                File.CreateSymbolicLink(linkPath, target);
            }
        }
        var rootEntries = Directory.GetFileSystemEntries(home).Order(StringComparer.Ordinal).ToArray();
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home, "--invalid-selected-metadata", allowFailure: true);
        Assert.Equal(0, result.Code);
        Assert.Contains("selected-metadata-refused-before-library-load", result.Output);
        Assert.Contains(defect switch
        {
            "nested-dll" => "cannot use obsolete package-directory library DLLs",
            "alias-metadata" => "noncanonical or duplicate installed package identities",
            "cycle" => "link cycle",
            _ => "resolves outside its root"
        }, result.Output);
        Assert.Equal(rootEntries, Directory.GetFileSystemEntries(home).Order(StringComparer.Ordinal));
        foreach (var (file, original) in before) Assert.Equal(original, File.ReadAllBytes(Path.Combine(home, file)));
        if (linkPath is not null)
        {
            FileSystemInfo link = Directory.Exists(linkPath) ? new DirectoryInfo(linkPath) : new FileInfo(linkPath);
            Assert.Equal(target, link.LinkTarget);
            if (defect is "outside-plugin-file" or "outside-root-file") Assert.Equal("outside-selected-home", File.ReadAllText(target!));
        }
    }

    [Fact]
    public async Task Contained_arbitrary_plugin_payloads_remain_supported_in_a_selected_execution_home()
    {
        var home = InstalledHome();
        var plugins = Path.Combine(home, "Plugins", "Custom");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "notes.txt"), "safe-contained-plugin-data");
        File.WriteAllText(Path.Combine(plugins, "package.xml"), "<Package Name=\"UnrelatedPlugin\" Version=\"1.0.0\"/>");
        var result = await Run(home, "HardwareTest.StandaloneVisa.ProcessFixture.dll", home);
        Assert.Equal(0, result.Code);
        Assert.Contains("managed-broker-bound-and-cleaned", result.Output);
        Assert.Equal("safe-contained-plugin-data", File.ReadAllText(Path.Combine(plugins, "notes.txt")));
    }
}
