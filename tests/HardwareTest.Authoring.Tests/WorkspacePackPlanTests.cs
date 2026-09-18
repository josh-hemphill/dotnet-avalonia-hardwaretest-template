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
        Assert.True(preview.IncludeTui);
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
        Assert.Contains("TUI", vm.RecipeAdvancedHint, StringComparison.Ordinal);
        Assert.False(vm.HasRawSteps);
        vm.CreateProgram("raw-banner");
        vm.ReplaceSelected(vm.SelectedProgram! with
        {
            Measure = [new RawStepNode("HangForeverStep", "<HangForever />")],
        });
        Assert.True(vm.HasRawSteps);
        Assert.Equal(1, vm.RawStepCount);
        Assert.Contains("Raw", vm.RawStepsBanner, StringComparison.Ordinal);
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
