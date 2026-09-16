using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class WorkspacePackerTests
{
    [Fact]
    public void Template_pack_files_exclude_board_demo()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        var files = PackageXmlRenderer.EnumeratePackFiles(workspace);
        Assert.Equal(PackageXmlRenderer.TemplatePackFiles, files);
        Assert.DoesNotContain(files, f => f.Contains("board-demo", StringComparison.OrdinalIgnoreCase));

        var xml = PackageXmlRenderer.Render(workspace);
        var doc = XDocument.Parse(xml);
        XNamespace ns = PackageXmlRenderer.PackageNs;
        var deps = doc.Descendants(ns + "PackageDependency")
            .Select(e => e.Attribute("Package")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(new[] { "OpenTAP", "HardwareTest Basic", "HardwareTest Mixins" }, deps);
    }

    [Fact]
    public void BlocksPack_is_true_for_unknown_types_and_tui_missing_catalog()
    {
        var unknown = new TuiCompatReport(
            [],
            [new RoundTripFinding("sample.TapPlan", TuiCompatCodes.TypeUnknown, "missing")]);
        Assert.True(unknown.BlocksPack());

        var driftOnly = new TuiCompatReport(
            [],
            [new RoundTripFinding("sample.TapPlan", TuiCompatCodes.XmlDrift, "noise")]);
        Assert.False(driftOnly.BlocksPack());

        var mixinDropped = new TuiCompatReport(
            [],
            [new RoundTripFinding("sample.TapPlan", TuiCompatCodes.MixinDropped, "mixin")]);
        Assert.True(mixinDropped.BlocksPack());

        var contractFail = new TuiCompatReport(
            [],
            [new RoundTripFinding("sample.TapPlan", TuiCompatCodes.ContractFail, "strict")]);
        Assert.True(contractFail.BlocksPack());

        var tuiMissing = new TuiCompatReport(
            [new CatalogDelta("HardwareTest.OpenTap.Plugins.Basic.AcquireVoltageStep", "Acquire", CatalogSides.TuiHome)],
            []);
        Assert.True(tuiMissing.BlocksPack());

        var tuiAppOnly = new TuiCompatReport(
            [new CatalogDelta("OpenTap.TUI.SomeView", "TUI", CatalogSides.TuiHome)],
            []);
        Assert.False(tuiAppOnly.BlocksPack());
    }

    [Fact]
    public void Pack_template_workspace_creates_tappackage_without_board_demo()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });
        var dist = NewTempDir();

        var manifest = WorkspacePacker.Pack(workspace, dist, new PackOptions { Home = home, Offline = true });

        Assert.Equal("HardwareTest Template Program", manifest.PackageName);
        Assert.Equal("0.1.0", manifest.Version);
        var package = Directory.EnumerateFiles(dist, "*.TapPackage").Single();
        Assert.Contains(Path.GetFileName(package), manifest.Files);
        Assert.Empty(Directory.EnumerateFiles(workspaceRoot, "*.TapPackage"));
        var shipped = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(dist, WorkspacePacker.ShipManifestFileName)),
            AuthoringJsonContext.Default.ShipManifest);
        Assert.NotNull(shipped);
        Assert.Equal(manifest.PackageName, shipped.PackageName);
        Assert.Equal(manifest.Version, shipped.Version);
        Assert.Equal(manifest.Files, shipped.Files);
        Assert.DoesNotContain(shipped.Files, f => f.Contains("shell-apps", StringComparison.Ordinal));

        using var zip = ZipFile.OpenRead(package);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
        Assert.Contains(names, n => n.EndsWith("sample.TapPlan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("sample.program.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("template.program.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("program.schema.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("board-demo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pack_fails_when_compat_blocks()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });

        var ex = Assert.Throws<AuthoringWorkspaceException>(() =>
            WorkspacePacker.Pack(
                workspace,
                NewTempDir(),
                new PackOptions { Home = home, Compat = new BlockingCompat(), Offline = true }));
        Assert.Contains(AuthoringPackCodes.CompatBlocked, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_copies_declared_plugin_tappackage()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var plugin = Path.Combine(workspaceRoot, "extra.TapPackage");
        File.WriteAllBytes(plugin, [0x50, 0x4B, 0x03, 0x04]);
        var manifestPath = Path.Combine(workspaceRoot, "authoring.json");
        var json = File.ReadAllText(manifestPath)
            .Replace("\"pluginProjects\": []", "\"pluginProjects\": [\"extra.TapPackage\"]", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, json);

        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });
        var dist = NewTempDir();
        var manifest = WorkspacePacker.Pack(workspace, dist, new PackOptions { Home = home, Offline = true });

        Assert.Contains("extra.TapPackage", manifest.Files);
        Assert.True(File.Exists(Path.Combine(dist, "extra.TapPackage")));
    }

    [Fact]
    public void Pack_rejects_plugin_csproj_instead_of_tappackage()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        File.WriteAllText(Path.Combine(workspaceRoot, "Extra.csproj"), "<Project />");
        var manifestPath = Path.Combine(workspaceRoot, "authoring.json");
        var json = File.ReadAllText(manifestPath)
            .Replace("\"pluginProjects\": []", "\"pluginProjects\": [\"Extra.csproj\"]", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, json);

        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });
        var ex = Assert.Throws<AuthoringWorkspaceException>(() =>
            WorkspacePacker.Pack(workspace, NewTempDir(), new PackOptions { Home = home, Offline = true }));
        Assert.Contains(AuthoringPackCodes.PluginMissing, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_app_manifest_entries_include_bake_time_marker()
    {
        Assert.Equal("shell-apps/Notes", WorkspacePacker.ShellAppDirectoryEntry("Notes"));
        Assert.Equal("shell-apps/Notes (bake-time)", WorkspacePacker.ShellAppBakeTimeEntry("Notes"));
        Assert.Contains("bake-time", WorkspacePacker.ShellAppBakeTimeEntry("Notes"), StringComparison.Ordinal);
    }

    private static string CopyTemplateWorkspace()
    {
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var dest = NewTempDir();
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        }

        return dest;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }

    private sealed class BlockingCompat : ITuiCompatChecker
    {
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
            => new(
                [],
                [new RoundTripFinding("sample.TapPlan", TuiCompatCodes.TypeUnknown, "blocked")]);
    }
}
