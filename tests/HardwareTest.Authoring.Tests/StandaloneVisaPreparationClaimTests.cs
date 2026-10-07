using System.IO.Compression;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class StandaloneVisaPreparationClaimTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Existing_standalone_aliases_are_rejected_before_owned_or_public_preparation_changes_any_bytes(bool owned, bool partialImport, bool metadataAlias)
    {
        var (workspace, home) = Prepare();
        var custom = Path.Combine(home.Root, "Plugins", "Custom"); Directory.CreateDirectory(custom);
        if (metadataAlias)
            File.Copy(Path.Combine(home.Root, "Packages", StandaloneVisaPackage.PackageName, "package.xml"), Path.Combine(custom, "package.xml"));
        else File.Copy(Path.Combine(home.Root, StandaloneVisaPackage.WrapperFileName), Path.Combine(custom, StandaloneVisaPackage.WrapperFileName));
        var archive = partialImport ? Archive(workspace, "Unrelated", "marker.txt", "complete safe import"u8.ToArray()) : null;
        var before = Snapshot(home);
        var options = new BootstrapOptions { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive };
        var bootstrap = new OpenTapHomeBootstrapper();
        var error = Assert.Throws<AuthoringWorkspaceException>(() =>
        {
            if (owned) bootstrap.BootstrapOwned(workspace, options); else bootstrap.Bootstrap(workspace, options);
        });
        Assert.Contains("canonical", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSnapshot(before, home);
    }

    [Theory]
    [InlineData(false, "HardwareTest.OpenTap.StandaloneVisa.dll")]
    [InlineData(true, "HardwareTest.OpenTap.StandaloneVisa.dll")]
    [InlineData(false, "InstrumentComponents.OpenTap.Visa.dll")]
    [InlineData(true, "InstrumentComponents.OpenTap.Visa.dll")]
    public void Late_TUI_import_cannot_publish_a_standalone_alias_after_counterpart_validation(bool partialImport, string payload)
    {
        var (workspace, home) = Prepare();
        var tui = Archive(workspace, "AliasTui", payload, File.ReadAllBytes(Path.Combine(home.Root, payload)));
        var archive = partialImport ? Archive(workspace, "Unrelated", "marker.txt", "complete safe import"u8.ToArray()) : null;
        var before = Snapshot(home);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(workspace,
            new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive, TuiPackagePath = tui }));
        Assert.Contains("canonical", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertSnapshot(before, home);
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
    }

    private static (AuthoringWorkspace Workspace, OpenTapHome Home) Prepare()
    {
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root); workspace.Manifest.SchemaVersion = 2;
        workspace.Manifest.Package.Name = "Standalone preparation claim control";
        workspace.Manifest.Dependencies.Add(new() { Package = PublishedInstrumentComponents.PackageName, Version = PublishedInstrumentComponents.Version });
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        Assert.True(StandaloneVisaReadiness.Assess(home).Available);
        return (workspace, home);
    }

    private static string Archive(AuthoringWorkspace workspace, string name, string file, byte[] bytes)
    {
        var path = Path.Combine(workspace.Root, name + ".TapPackage");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
            writer.Write($"<Package Name=\"{name}\" Version=\"1.0.0\"><Files><File Path=\"{file}\"/></Files></Package>");
        using (var target = zip.CreateEntry(file).Open()) target.Write(bytes);
        return path;
    }

    private static Dictionary<string, byte[]> Snapshot(OpenTapHome home) => Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);

    private static void AssertSnapshot(Dictionary<string, byte[]> before, OpenTapHome home)
    {
        var after = Snapshot(home);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }
}
