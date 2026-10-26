using System.IO.Compression;
using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringPackageDownloadTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ht-download-tests-" + Guid.NewGuid().ToString("N"));
    public AuthoringPackageDownloadTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Preparation_downloads_required_and_transitive_packages_reuses_installed_and_skips_optional()
    {
        var manifest = new AuthoringManifest
        {
            Dependencies = [new() { Package = "Required fixture", Version = "^1.0.0" }],
            OptionalDependencies = [new() { Package = "Optional fixture", Version = "^1.0.0" }]
        };
        var workspace = new AuthoringWorkspace(root, manifest, []);
        var requested = new List<string>();
        var stages = new List<string>();
        var bootstrap = new OpenTapHomeBootstrapper
        {
            DownloadPackage = (requirement, repository, path) =>
            {
                Assert.Equal("https://packages.example.test/feed", repository);
                requested.Add(requirement.Package);
                WriteArchive(path, requirement.Package, "1.2.0", requirement.Package == "Required fixture" ? "Transitive fixture" : null);
            }
        };
        var options = new BootstrapOptions { HomeDirectory = Path.Combine(root, "selected"), Offline = false,
            PackageRepositoryUrl = "https://packages.example.test/feed", Progress = stages.Add };
        var home = bootstrap.Bootstrap(workspace, options);
        Assert.Equal(["Required fixture", "Transitive fixture"], requested);
        Assert.True(AuthoringEnvironmentAssessment.Packages(manifest, home).Single(p => !p.Optional).Satisfied);
        Assert.False(AuthoringEnvironmentAssessment.Packages(manifest, home).Single(p => p.Optional).Satisfied);
        Assert.Contains(stages, stage => stage.Contains("Downloading Required fixture"));
        Assert.Empty(Directory.GetDirectories(home.Root, ".package-download-*"));
        bootstrap.Bootstrap(workspace, options);
        Assert.Equal(2, requested.Count);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("invalid-archive")]
    [InlineData("transitive")]
    public void Failed_download_preserves_selected_home_and_explains_manual_fallback(string failure)
    {
        var home = Path.Combine(root, "selected");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "marker.txt"), "preserved");
        var workspace = new AuthoringWorkspace(root, new() { Dependencies = [new() { Package = "Required fixture", Version = "^1.0.0" }] }, []);
        var bootstrap = new OpenTapHomeBootstrapper
        {
            DownloadPackage = (requirement, _, path) =>
            {
                if (failure == "network" || requirement.Package == "Missing transitive") throw new IOException("Repository unavailable");
                if (failure == "invalid-archive") { File.WriteAllText(path, "partial archive"); return; }
                WriteArchive(path, failure == "identity" ? "Wrong fixture" : requirement.Package,
                    failure == "version" ? "2.0.0" : "1.2.0", failure == "transitive" ? "Missing transitive" : null);
            }
        };
        var error = Assert.Throws<AuthoringWorkspaceException>(() => bootstrap.Bootstrap(workspace, new() { HomeDirectory = home, Offline = false }));
        Assert.Contains("Import offline package", error.Message);
        if (failure is "identity" or "version") Assert.Contains("does not match", error.Message);
        if (failure is "network" or "transitive") Assert.Contains("Repository unavailable", error.Message);
        Assert.Equal(["marker.txt"], Directory.GetFiles(home, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(home, "marker.txt")));
    }

    [Fact]
    public void Offline_preparation_never_contacts_the_repository()
    {
        var workspace = new AuthoringWorkspace(root, new() { Dependencies = [new() { Package = "Required fixture", Version = "^1.0.0" }] }, []);
        var contacted = false;
        var bootstrap = new OpenTapHomeBootstrapper { DownloadPackage = (_, _, _) => contacted = true };
        Assert.Throws<AuthoringWorkspaceException>(() => bootstrap.Bootstrap(workspace, new() { HomeDirectory = Path.Combine(root, "selected"), Offline = true }));
        Assert.False(contacted);
    }

    [Theory]
    [InlineData("http://packages.example.test")]
    [InlineData("https://user:password@packages.example.test")]
    [InlineData("https://packages.example.test/?token=secret")]
    [InlineData("file:///tmp/packages")]
    [InlineData("")]
    public void Invalid_repository_keeps_manual_fallback_available(string repository)
        => Assert.Contains("Manual download", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringPackageDownloads.RepositoryUri(repository)).Message);

    internal static void WriteArchive(string path, string name, string version, string? dependency = null)
    {
        XNamespace ns = "http://opentap.io/schemas/package";
        var payload = $"Packages/{name}/payload.txt";
        var metadata = new XElement(ns + "Package", new XAttribute("Name", name), new XAttribute("Version", version),
            new XAttribute("Architecture", "AnyCPU"), new XAttribute("OS", "Windows,Linux,MacOS"),
            new XElement(ns + "Files", new XElement(ns + "File", new XAttribute("Path", payload))),
            new XElement(ns + "Dependencies", dependency is null ? null : new XElement(ns + "Package", new XAttribute("Name", dependency), new XAttribute("Version", "^1.0.0"))));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry($"Packages/{name}/package.xml").Open())) writer.Write(metadata.ToString());
        using (var writer = new StreamWriter(archive.CreateEntry(payload).Open())) writer.Write(name + " payload");
    }
}
