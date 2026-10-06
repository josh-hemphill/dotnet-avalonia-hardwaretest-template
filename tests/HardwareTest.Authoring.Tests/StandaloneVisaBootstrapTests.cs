using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class StandaloneVisaBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-counterpart-bootstrap-" + Guid.NewGuid().ToString("N"));
    private AuthoringWorkspace Workspace(bool library = true) => new(_root, new AuthoringManifest
    { Dependencies = library ? [new() { Package = PublishedInstrumentComponents.PackageName, Version = PublishedInstrumentComponents.Version }] : [] }, []);
    private OpenTapHome Prepare(AuthoringWorkspace workspace, string? overridePath = null) => new OpenTapHomeBootstrapper().Bootstrap(workspace,
        new() { HomeDirectory = Path.Combine(_root, "home"), Offline = true, InstrumentComponentsPackagePath = overridePath });

    [Fact]
    public void Declared_library_prepares_counterpart_and_repairs_substituted_provider_without_loading_it()
    {
        var home = Prepare(Workspace());
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "InstrumentComponents.OpenTap.Visa");
        var provider = Path.Combine(home.Root, "InstrumentComponents.OpenTap.Visa.dll");
        var expected = File.ReadAllBytes(provider);
        File.WriteAllText(provider, "substituted");
        Assert.False(StandaloneVisaReadiness.Assess(home).Available);
        Prepare(Workspace());
        Assert.Equal(expected, File.ReadAllBytes(provider));
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
    }

    [Fact]
    public void Unsupported_selected_dependency_versions_fail_preparation_and_preserve_home()
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", PublishedInstrumentComponents.PackageName, "package.xml");
        File.WriteAllText(metadata, File.ReadAllText(metadata).Replace("Version=\"0.1.1\"", "Version=\"0.1.0\"", StringComparison.Ordinal));
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace(), "missing-override.TapPackage"));
        Assert.Contains("unsupported", error.Message);
        Assert.Contains("unsupported", StandaloneVisaReadiness.Assess(home).Reason);
        Assert.False(AuthoringEnvironmentAssessment.Packages(Workspace().Manifest, home).Single().Satisfied);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_opentap_version_fails_counterpart_preparation_transactionally(bool explicitPartialImport)
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var text = File.ReadAllText(metadata);
        var document = System.Xml.Linq.XDocument.Parse(text);
        document.Root!.SetAttributeValue("Version", "9.34.0");
        File.WriteAllText(metadata, document.ToString());
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        string? archive = null;
        if (explicitPartialImport)
        {
            archive = Path.Combine(_root, "unrelated.TapPackage");
            using var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
                writer.Write("<Package Name=\"Unrelated\" Version=\"1.0.0\"><Files><File Path=\"payload.txt\"/></Files></Package>");
            using (var writer = new StreamWriter(zip.CreateEntry("payload.txt").Open())) writer.Write("unrelated payload");
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(Workspace(),
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive }));
        Assert.Contains("unsupported", error.Message);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Theory]
    [InlineData("9.34.0", "9.35.0")]
    [InlineData("9.35.0", "9.34.0")]
    [InlineData("9.35.0", "9.35.0")]
    public void Duplicate_opentap_identity_cannot_spoof_current_dependency_readiness_or_preparation(string canonicalVersion, string aliasVersion)
    {
        var home = Prepare(Workspace());
        var metadata = Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml");
        var document = System.Xml.Linq.XDocument.Load(metadata);
        document.Root!.SetAttributeValue("Version", canonicalVersion);
        document.Save(metadata);
        var alias = Path.Combine(home.Root, "Packages", "OtherOpenTap");
        Directory.CreateDirectory(alias);
        File.WriteAllText(Path.Combine(alias, "package.xml"), $"<Package Name=\"OpenTAP\" Version=\"{aliasVersion}\"><Files/></Package>");
        var before = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        Assert.False(StandaloneVisaReadiness.Assess(home).Available);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => Prepare(Workspace()));
        Assert.Contains("unsupported", error.Message);
        var after = Directory.GetFiles(home.Root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(home.Root, path));
        Assert.Equal(before.Keys.Order(), after.Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(home.Root, file.Key)));
    }

    [Fact]
    public void Ordinary_mock_home_does_not_install_the_standalone_counterpart()
    {
        var home = Prepare(Workspace(library: false));
        Assert.DoesNotContain(OpenTapHomeBootstrapper.ListInstalledPackages(home), package => package.Name == StandaloneVisaPackage.PackageName);
        Assert.False(File.Exists(Path.Combine(home.Root, StandaloneVisaPackage.WrapperFileName)));
        Assert.Contains("mock plans do not need it", StandaloneVisaReadiness.Assess(home).Reason);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
