using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class WorkspacePackPlanTests
{
    [Fact]
    public void Describe_template_workspace_shows_opentap_pin_and_pack_files()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        var preview = WorkspacePackPlan.Describe(workspace);
        Assert.Equal("HardwareTest Template Program", preview.ProgramPackageName);
        Assert.Equal("^9.32.2", preview.OpenTapVersionPin);
        Assert.Contains(preview.ProgramPackContents, file => file.RelativePath == "sample.TapPlan");
        Assert.Contains(preview.PackageDependencies, dep => dep.Package == "OpenTAP");
        Assert.Empty(preview.PluginProjects);
        Assert.Empty(preview.ShellAppProjects);
        Assert.False(preview.IncludeTui);
    }

    [Fact]
    public void Describe_marks_missing_plugin_and_found_shell_app()
    {
        var root = CopyTemplateWorkspace();
        File.WriteAllText(Path.Combine(root, "Tiny.csproj"), "<Project />");
        var manifestPath = Path.Combine(root, "authoring.json");
        var json = File.ReadAllText(manifestPath)
            .Replace("\"pluginProjects\": []", "\"pluginProjects\": [\"missing.TapPackage\"]", StringComparison.Ordinal)
            .Replace("\"shellAppProjects\": []", "\"shellAppProjects\": [\"Tiny.csproj\"]", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, json);
        var preview = WorkspacePackPlan.Describe(AuthoringWorkspaceLoader.Load(root));
        var plugin = Assert.Single(preview.PluginProjects);
        Assert.False(plugin.Exists);
        Assert.Equal("TapPackage", plugin.ExpectedKind);
        var shell = Assert.Single(preview.ShellAppProjects);
        Assert.True(shell.Exists);
        Assert.Equal("csproj", shell.ExpectedKind);
        Assert.Contains("Tiny.csproj", shell.DisplayText, StringComparison.Ordinal);
        Assert.Contains("found", shell.DisplayText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing", plugin.DisplayText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewModel_open_exposes_pack_preview_and_raw_banner()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-packprev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(dest);
        Assert.Equal("^9.32.2", vm.PackPreview.OpenTapVersionPin);
        Assert.Contains("OpenTAP", vm.OpenTapPinText, StringComparison.Ordinal);
        Assert.Contains("bake-time", AuthoringChrome.ShipPurpose, StringComparison.Ordinal);
        Assert.Contains("Raw", vm.AddRecipeToolTip, StringComparison.Ordinal);
        Assert.DoesNotContain("TUI", vm.AddRecipeToolTip, StringComparison.Ordinal);
        Assert.False(vm.HasRawSteps);
        vm.CreateProgram("raw-banner");
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Measure = [new RawStepNode("HangForeverStep", "<HangForever />")],
        });
        Assert.True(vm.HasRawSteps);
        Assert.Equal(1, vm.RawStepCount);
        Assert.Contains("Raw", vm.RawStepsBanner, StringComparison.Ordinal);
        Assert.False(WorkspacePackPlan.Empty.IncludeTui);
    }

    [Fact]
    public void TryReadShipManifest_round_trips_dependencies_and_fail_softs_bad_json()
    {
        var dist = Path.Combine(Path.GetTempPath(), "ht-ship-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dist);
        var written = new ShipManifest(
            "Demo",
            "1.0.0",
            ["Demo.TapPackage", WorkspacePacker.ShellAppBakeTimeEntry("Notes")],
            [new ShipDependency("OpenTAP", "^9.32.2"), new ShipDependency("Extra", "1.0.0", true, "linux")]);
        File.WriteAllText(
            Path.Combine(dist, WorkspacePacker.ShipManifestFileName),
            System.Text.Json.JsonSerializer.Serialize(written, AuthoringJsonContext.Default.ShipManifest));
        var json = File.ReadAllText(Path.Combine(dist, WorkspacePacker.ShipManifestFileName));
        Assert.Contains("\"dependencies\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("resolvedDependencies", json, StringComparison.OrdinalIgnoreCase);
        var loaded = WorkspacePackPlan.TryReadShipManifest(dist);
        Assert.NotNull(loaded);
        Assert.Equal("Demo", loaded.PackageName);
        Assert.Contains(loaded.ResolvedDependencies, dep => dep.Package == "OpenTAP");
        Assert.Contains(loaded.ResolvedDependencies, dep => dep.Optional && dep.Package == "Extra");

        File.WriteAllText(
            Path.Combine(dist, WorkspacePacker.ShipManifestFileName),
            """{"packageName":"Legacy","version":"1.0.0","files":[]}""");
        var legacy = WorkspacePackPlan.TryReadShipManifest(dist);
        Assert.NotNull(legacy);
        Assert.Empty(legacy.ResolvedDependencies);

        File.WriteAllText(Path.Combine(dist, WorkspacePacker.ShipManifestFileName), "{not-json");
        Assert.Null(WorkspacePackPlan.TryReadShipManifest(dist));
    }

    [Fact]
    public void Describe_reads_last_ship_manifest_from_dist()
    {
        var root = CopyTemplateWorkspace();
        var dist = Path.Combine(root, WorkspacePackPlan.DistDirectoryName);
        Directory.CreateDirectory(dist);
        File.WriteAllText(
            Path.Combine(dist, WorkspacePacker.ShipManifestFileName),
            System.Text.Json.JsonSerializer.Serialize(
                new ShipManifest(
                    "HardwareTest Template Program",
                    "0.1.0",
                    ["HardwareTest Template Program 0.1.0.TapPackage", WorkspacePacker.ShellAppBakeTimeEntry("Notes")],
                    [new ShipDependency("OpenTAP", "^9.32.2")]),
                AuthoringJsonContext.Default.ShipManifest));
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        Assert.True(vm.HasLastPack);
        Assert.True(vm.HasLastShippedBakeTimeFiles);
        Assert.True(vm.HasPackageDependencies);
        Assert.Contains("OpenTAP", vm.PackPreview.LastShipManifest!.ResolvedDependencies.Select(d => d.Package));
    }

    [Fact]
    public void OpenTap_pin_can_come_from_optional_dependencies()
    {
        var root = CopyTemplateWorkspace();
        var manifest = new AuthoringManifest
        {
            SchemaVersion = 1,
            DisplayName = "opt-pin",
            PlansDirectory = ".",
            Package = new AuthoringPackageSpec { Name = "Opt", Version = "0.1.0" },
            OptionalDependencies =
            [
                new AuthoringOptionalDependency { Package = "OpenTAP", Version = "^9.99.0", When = "test" },
            ],
        };
        AuthoringWorkspaceLoader.SaveManifest(root, manifest);
        var preview = WorkspacePackPlan.Describe(AuthoringWorkspaceLoader.Load(root));
        Assert.Equal("^9.99.0", preview.OpenTapVersionPin);
        Assert.Contains(WorkspacePackPlan.ShipDependencies(manifest), dep => dep.Optional && dep.Package == "OpenTAP");
    }

    [Fact]
    public void WithLastPack_overlays_output_directory()
    {
        var preview = WorkspacePackPlan.Empty;
        var overlay = WorkspacePackPlan.WithLastPack(
            preview,
            new ShipManifest("Demo", "1.0.0", ["Demo.TapPackage"], [new ShipDependency("OpenTAP", "^9.32.2")]),
            Path.GetTempPath());
        Assert.Equal("Demo", overlay.LastShipManifest?.PackageName);
        Assert.False(string.IsNullOrWhiteSpace(overlay.LastOutputDirectory));
    }

    private static string CopyTemplateWorkspace()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-packplan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        foreach (var file in Directory.EnumerateFiles(src))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        }

        return dest;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HardwareTest.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}
