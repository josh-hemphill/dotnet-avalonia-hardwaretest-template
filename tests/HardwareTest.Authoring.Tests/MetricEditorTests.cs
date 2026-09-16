using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class MetricEditorTests
{
    [Fact]
    public void Palette_matches_getting_started_types_and_omits_dialog()
    {
        var ids = AuthoringRecipeCatalog.Palette.Select(r => r.Id).ToArray();
        Assert.Contains(AuthoringRecipeIds.TestGroup, ids);
        Assert.Contains(AuthoringRecipeIds.Identity, ids);
        Assert.Contains(AuthoringRecipeIds.Prompt, ids);
        Assert.Contains(AuthoringRecipeIds.Input, ids);
        Assert.Contains(AuthoringRecipeIds.Acquire, ids);
        Assert.Contains(AuthoringRecipeIds.MeanGte, ids);
        Assert.Contains(AuthoringRecipeIds.BandScalar, ids);
        Assert.Contains(AuthoringRecipeIds.SeriesCompliance, ids);
        Assert.Contains(AuthoringRecipeIds.Repeat, ids);
        Assert.Contains(AuthoringRecipeIds.StationHealth, ids);
        Assert.Contains(AuthoringRecipeIds.Shutdown, ids);
        Assert.False(AuthoringRecipeCatalog.PaletteContainsDialog());
        Assert.DoesNotContain(
            AuthoringRecipeCatalog.Palette,
            recipe => recipe.Title.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
                      || recipe.Id.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
                      || recipe.Title.Contains("Hang Forever", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adding_vdc_mean_scalar_saves_plan_that_validates_and_decompiles()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("metric-ui");
        Assert.Equal("What do you want to measure? Pick a recipe.", vm.MeasureHint);

        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        Assert.Equal("VDC.mean", vm.ChannelKey);
        Assert.Equal(PresentationRoles.Scalar, vm.DisplayRole);
        Assert.Equal(PresentationTileKind.Scalar, vm.Preview.TileKind);
        Assert.False(string.IsNullOrWhiteSpace(vm.Threshold));

        vm.VisaAddress = "MOCK::CUSTOM";
        vm.Apply();

        var report = vm.Validate(strict: true);
        Assert.False(report.HasErrors, string.Join("; ", vm.Findings.Select(f => $"{f.Code}: {f.Message}")));

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("metric-ui");
        Assert.Contains(reloaded.MeasureTree, line => line.Contains("VDC.mean", StringComparison.Ordinal));
        Assert.Contains(
            AuthoringRecipeCatalog.EnumerateMetrics(reloaded.SelectedProgram!.Measure),
            metric => metric.ChannelKey == "VDC.mean"
                      && metric.Source is AlgorithmSource algorithm
                      && algorithm.AlgorithmId == AuthoringFunctionIds.BasicMeanGte);
        Assert.Equal("MOCK::CUSTOM", reloaded.VisaAddress);
        Assert.Equal(PresentationTileKind.Scalar, reloaded.Preview.TileKind);
    }

    [Fact]
    public void Scalar_without_limits_refuses_apply()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("no-limits");
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.Threshold = string.Empty;

        var ex = Assert.Throws<AuthoringWorkspaceException>(() => vm.Apply());
        Assert.Contains(AuthoringCompileCodes.MissingLimits, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "no-limits.TapPlan")));
    }

    [Fact]
    public void Visa_address_stays_writable_on_the_instrument_slot()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("visa");
        Assert.Equal("MOCK::INSTR0", vm.VisaAddress);
        vm.VisaAddress = "TCPIP0::1.2.3.4::INSTR";
        Assert.Equal("TCPIP0::1.2.3.4::INSTR", vm.VisaAddress);
        Assert.Equal("TCPIP0::1.2.3.4::INSTR", vm.SelectedProgram!.Instruments[0].VisaAddress);
    }

    [Fact]
    public void Repeat_recipe_wraps_the_last_metric_and_raw_nodes_stay()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("repeat");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Repeat);
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure));
        Assert.Equal(2, repeat.Count);
        Assert.Equal("VDC", Assert.IsType<MetricNode>(Assert.Single(repeat.Children)).Metric.ChannelKey);
        Assert.Contains(vm.MeasureTree, line => line.Contains("Repeat", StringComparison.Ordinal));
    }

    private static string EmptyWorkspace()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-metric-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
        var schema = Path.Combine(src, "authoring.schema.json");
        if (File.Exists(schema))
        {
            File.Copy(schema, Path.Combine(dest, "authoring.schema.json"));
        }

        return dest;
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
