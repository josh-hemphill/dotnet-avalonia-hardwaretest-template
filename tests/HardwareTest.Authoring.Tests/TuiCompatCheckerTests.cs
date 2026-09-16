using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class TuiCompatCheckerTests
{
    [Fact]
    public void Sample_plan_round_trip_does_not_block_pack()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = Bootstrap(workspace);
        var sample = workspace.TapPlanPaths.Single(p =>
            string.Equals(Path.GetFileName(p), "sample.TapPlan", StringComparison.OrdinalIgnoreCase));
        var before = TuiCompatChecker.ReadChannelKeys(sample);
        Assert.Contains("VDC", before);
        Assert.Contains("VDC.mean", before);

        var after = TuiCompatChecker.RoundTripChannelKeys(sample, home);
        Assert.Contains("VDC", after);
        Assert.Contains("VDC.mean", after);
        Assert.Equal(
            before.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            after.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Contains(home.Root, TuiCompatChecker.EnumeratePluginDirectories(home));

        var report = new TuiCompatChecker().Compare(workspace, home, home);
        Assert.False(report.BlocksPack());
        Assert.DoesNotContain(report.RoundTrips, f => f.Code == TuiCompatCodes.TypeUnknown);
        Assert.DoesNotContain(report.RoundTrips, f => f.Code == TuiCompatCodes.MixinDropped);
        Assert.Contains(
            TuiCompatChecker.ScanCatalog(home).Keys,
            name => name.Contains("PresentationMixinBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_xml_type_is_type_unknown()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var sample = Path.Combine(workspaceRoot, "sample.TapPlan");
        var xml = File.ReadAllText(sample)
            .Replace(
                "HardwareTest.OpenTap.Plugins.Basic.AcquireVoltageStep",
                "HardwareTest.Fake.UnknownStep",
                StringComparison.Ordinal);
        File.WriteAllText(sample, xml);
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = Bootstrap(workspace);
        var report = new TuiCompatChecker().Compare(workspace, home, home);
        Assert.Contains(report.RoundTrips, f => f.Code == TuiCompatCodes.TypeUnknown);
        Assert.True(report.BlocksPack());
    }

    [Fact]
    public void Authoring_only_mixin_types_block_pack_via_catalog()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var authoring = Bootstrap(workspace);
        var tui = new OpenTapHome(CopyDirectory(authoring.Root, NewTempDir()));
        var mixins = Path.Combine(tui.Root, "Packages", "HardwareTest Mixins");
        if (Directory.Exists(mixins))
        {
            Directory.Delete(mixins, recursive: true);
        }

        var report = new TuiCompatChecker().Compare(workspace, authoring, tui);
        Assert.Contains(
            report.Catalog,
            d => d.MissingOn == CatalogSides.TuiHome
                 && d.TypeName.Contains("PresentationMixinBuilder", StringComparison.Ordinal));
        Assert.True(report.BlocksPack());

        var ex = Assert.Throws<AuthoringWorkspaceException>(() =>
            WorkspacePacker.Pack(
                workspace,
                NewTempDir(),
                new PackOptions
                {
                    Home = authoring,
                    TuiHome = tui,
                    Offline = true,
                    Compat = new TuiCompatChecker(),
                }));
        Assert.Contains(AuthoringPackCodes.CompatBlocked, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tui_only_non_app_types_do_not_block_pack()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var tui = Bootstrap(workspace);
        var authoring = new OpenTapHome(CopyDirectory(tui.Root, NewTempDir()));
        var mixins = Path.Combine(authoring.Root, "Packages", "HardwareTest Mixins");
        if (Directory.Exists(mixins))
        {
            Directory.Delete(mixins, recursive: true);
        }

        var report = new TuiCompatChecker().Compare(workspace, authoring, tui);
        Assert.Contains(
            report.Catalog,
            d => d.MissingOn == CatalogSides.AuthoringHome
                 && d.TypeName.Contains("PresentationMixinBuilder", StringComparison.Ordinal));
        Assert.DoesNotContain(
            report.Catalog,
            d => d.MissingOn == CatalogSides.TuiHome
                 && d.TypeName.Contains("PresentationMixinBuilder", StringComparison.Ordinal));
        Assert.False(report.BlocksPack());
    }

    private static OpenTapHome Bootstrap(AuthoringWorkspace workspace)
        => new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });

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

    private static string CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(dest, Path.GetRelativePath(source, file)), overwrite: true);
        }

        return dest;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-tui-" + Guid.NewGuid().ToString("N"));
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
}
