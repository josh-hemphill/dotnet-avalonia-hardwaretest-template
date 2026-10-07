using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class PublishedInstrumentComponentsTests
{
    [Fact]
    public void Embedded_archive_is_the_exact_published_release()
    {
        using var stream = PublishedInstrumentComponents.OpenArchive();
        Assert.Equal(PublishedInstrumentComponents.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal(new[] { "InstrumentComponents.OpenTap.dll", "InstrumentComponents.dll", "Packages/InstrumentComponents.OpenTap/package.xml" },
            zip.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        using var metadata = zip.GetEntry("Packages/InstrumentComponents.OpenTap/package.xml")!.Open();
        var package = XDocument.Load(metadata).Root!;
        Assert.Equal(PublishedInstrumentComponents.PackageName, (string?)package.Attribute("Name"));
        Assert.Equal(PublishedInstrumentComponents.Version, (string?)package.Attribute("Version"));
    }

    [Fact]
    public void Transitive_output_payload_matches_archive_bytes()
    {
        using var stream = PublishedInstrumentComponents.OpenArchive();
        using var actual = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "PublishedArtifacts", PublishedInstrumentComponents.ArchiveName));
        Assert.Equal(SHA256.HashData(stream), SHA256.HashData(actual));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "InstrumentComponents.OpenTap.dll")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "InstrumentComponents.dll")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Packages", PublishedInstrumentComponents.PackageName, "package.xml")));

    }

    [Fact]
    public void Materialization_is_exact_and_does_not_overwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".TapPackage");
        try
        {
            PublishedInstrumentComponents.MaterializeArchive(path);
            using (var stream = File.OpenRead(path))
                Assert.Equal(PublishedInstrumentComponents.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
            var original = File.ReadAllBytes(path);
            Assert.Throws<IOException>(() => PublishedInstrumentComponents.MaterializeArchive(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Archive_streams_are_read_only_and_independently_owned()
    {
        using var first = PublishedInstrumentComponents.OpenArchive();
        using var second = PublishedInstrumentComponents.OpenArchive();
        Assert.False(first.CanWrite);
        Assert.False(second.CanWrite);
        Assert.Throws<NotSupportedException>(() => first.WriteByte(0));
        first.ReadByte();
        Assert.Equal(0, second.Position);
        first.Dispose();
        Assert.Equal(PublishedInstrumentComponents.Sha256, Convert.ToHexStringLower(SHA256.HashData(second)));
    }

    [Fact]
    [Trait("Category", "AuthoringIntegration")]
    public async Task Bundled_archive_keeps_cold_authoring_library_free()
    {
        var root = Path.Combine(Path.GetTempPath(), "ht-published-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var draft = new AuthoringPlanInitializer().Construct(new("isolated")
            {
                Instruments = [new InstrumentRef("Supply", typeof(MockDmmInstrument).FullName!, "MOCK::SUPPLY")],
                IdentityInstrumentSlot = "Supply"
            }).Draft;
            var plan = Path.Combine(root, "isolated.TapPlan");
            new PlanCompiler().Save(draft, plan);
            var xml = XDocument.Load(plan);
            foreach (var instrument in xml.Descendants().Where(element => element.Name.LocalName == "Instrument" && element.Attribute("type") is not null))
                instrument.SetAttributeValue("type", "InstrumentComponents.OpenTap.DcPowerSupplyInstrument");
            xml.Save(plan);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll"));
            start.ArgumentList.Add("--cold-library-import");
            start.ArgumentList.Add(plan);
            start.ArgumentList.Add(Path.Combine(root, "isolated.authoring.json"));
            using var child = Process.Start(start)!;
            try
            {
                var output = child.StandardOutput.ReadToEndAsync();
                var error = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(child.ExitCode == 0, await error);
                Assert.Contains("cold-library-source-preserved", await output);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

}
