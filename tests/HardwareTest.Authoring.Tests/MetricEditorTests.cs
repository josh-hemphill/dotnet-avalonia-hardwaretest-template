using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
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
        Assert.Contains(AuthoringRecipeIds.Formula, ids);
        Assert.Contains(AuthoringRecipeIds.TransferFunction, ids);
        Assert.Contains(AuthoringRecipeCatalog.Palette, recipe => recipe.Title.Contains("Transfer function", StringComparison.Ordinal));
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
        Assert.Equal(AuthoringChrome.EmptyMeasureHint, vm.MeasureHint);

        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        Assert.Equal("VDC.mean", vm.ChannelKey);
        Assert.Equal(PresentationRoles.Scalar, vm.DisplayRole);
        Assert.Equal(PresentationTileKind.Scalar, vm.Preview.TileKind);
        Assert.True(vm.PreviewChrome.IsGauge);
        Assert.False(vm.PreviewChrome.IsChart);
        Assert.Equal("VDC.mean", vm.PreviewChrome.MetricKey);
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

    [Fact]
    public void Series_compliance_keeps_voltage_band_limits_on_save()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("series");
        vm.ApplyRecipe(AuthoringRecipeIds.SeriesCompliance);
        Assert.Equal("1.1", vm.LimitLow);
        Assert.Equal("1.4", vm.LimitHigh);
        vm.Apply();

        var xml = File.ReadAllText(Path.Combine(root, "series.TapPlan"));
        Assert.Contains("1.1", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<LimitLow>100</LimitLow>", xml, StringComparison.OrdinalIgnoreCase);

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("series");
        var metric = Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(reloaded.SelectedProgram!.Measure));
        Assert.Equal("series.inband.pct", metric.ChannelKey);
        Assert.Equal(1.1, metric.Limits?.Low);
        Assert.Equal(1.4, metric.Limits?.High);
        Assert.Equal("1.20,1.22,1.21", GetSetting(metric, "Values"));
    }

    [Fact]
    public void Station_health_scalar_round_trips_limits_and_can_be_saved_again()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("health");
        vm.ApplyRecipe(AuthoringRecipeIds.StationHealth);
        Assert.Equal(PresentationRoles.Scalar, vm.DisplayRole);
        vm.Apply();

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("health");
        var metric = Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(reloaded.SelectedProgram!.Measure));
        Assert.Equal("cal.dc.offset", metric.ChannelKey);
        Assert.Equal(-0.01, metric.Limits?.Low);
        Assert.Equal(0.01, metric.Limits?.High);
        reloaded.Apply();
        Assert.Null(reloaded.Error);
        Assert.True(File.Exists(Path.Combine(root, "health.TapPlan")));
    }

    [Fact]
    public void Raw_step_survives_repeat_save_and_reload()
    {
        var root = EmptyWorkspace();
        var xml = HangForeverXml();
        var draft = AuthoringRecipeCatalog.CreateProgram("raw");
        draft = draft with { Measure = [new RawStepNode(typeof(HangForeverStep).FullName!, xml)] };
        draft = AuthoringRecipeCatalog.Apply(draft, AuthoringRecipeIds.Repeat);
        new PlanCompiler().Save(draft, Path.Combine(root, "raw.TapPlan"));

        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.SelectProgram("raw");
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(vm.SelectedProgram!.Measure));
        var raw = Assert.IsType<RawStepNode>(Assert.Single(repeat.Children));
        Assert.Contains("HangForeverStep", raw.TypeName, StringComparison.Ordinal);
        Assert.Contains(vm.MeasureTree, line => line.Contains("HangForeverStep", StringComparison.Ordinal));
        vm.Apply();

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("raw");
        var loadedRepeat = Assert.IsType<RepeatNode>(Assert.Single(reloaded.SelectedProgram!.Measure));
        Assert.Contains(
            "HangForeverStep",
            Assert.IsType<RawStepNode>(Assert.Single(loadedRepeat.Children)).TypeName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_keeps_unsaved_created_programs()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("saved");
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.CreateProgram("pending");
        vm.SelectProgram("saved");
        vm.Apply();

        Assert.Contains(vm.Programs, p => p.PlanId == "saved");
        Assert.Contains(vm.Programs, p => p.PlanId == "pending");
        Assert.Equal("saved", vm.SelectedProgram?.PlanId);
        Assert.False(File.Exists(Path.Combine(root, "pending.TapPlan")));
    }

    [Fact]
    public void Apply_keeps_dirty_edits_on_other_saved_programs()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("alpha");
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.Apply();
        vm.CreateProgram("beta");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.Apply();

        vm.SelectProgram("alpha");
        vm.DisplayName = "dirty-alpha";
        vm.ApplyRecipe(AuthoringRecipeIds.BandScalar);
        vm.SelectProgram("beta");
        vm.Apply();

        vm.SelectProgram("alpha");
        Assert.Equal("dirty-alpha", vm.DisplayName);
        Assert.Contains(
            AuthoringRecipeCatalog.EnumerateMetrics(vm.SelectedProgram!.Measure),
            metric => metric.ChannelKey == "rail.mean");
        Assert.False(
            File.ReadAllText(Path.Combine(root, "alpha.program.json")).Contains("dirty-alpha", StringComparison.Ordinal));
    }

    [Fact]
    public void Formula_mean_vdc_parses_and_preview_is_scalar()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("formula");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        Assert.Equal("mean(VDC)", vm.FormulaSource);
        Assert.True(string.IsNullOrWhiteSpace(vm.FormulaError), vm.FormulaError);
        Assert.Contains("Mean GTE", vm.FormulaSaveNote, StringComparison.Ordinal);
        Assert.Equal(PresentationTileKind.Scalar, vm.Preview.TileKind);
        vm.FormulaSource = "fft(VDC)";
        Assert.Contains(AuthoringCompileCodes.FormulaParse, vm.FormulaError, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(vm.FormulaSaveNote), vm.FormulaSaveNote);
        vm.FormulaSource = "mean(VDC)";
        Assert.True(string.IsNullOrWhiteSpace(vm.FormulaError), vm.FormulaError);
        Assert.Contains("Mean GTE", vm.FormulaSaveNote, StringComparison.Ordinal);
        vm.Apply();
        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("formula");
        Assert.Contains(
            AuthoringRecipeCatalog.EnumerateMetrics(reloaded.SelectedProgram!.Measure),
            metric => metric.ChannelKey == "VDC.mean"
                      && metric.Source is AlgorithmSource algorithm
                      && algorithm.AlgorithmId == AuthoringFunctionIds.BasicMeanGte);
    }

    [Fact]
    public void Formula_source_does_not_replace_measure_source()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("acquire");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram!.Measure)).Metric.Source);
        Assert.True(vm.PreviewChrome.IsChart);
        Assert.False(vm.PreviewChrome.IsGauge);
        Assert.NotEmpty(vm.PreviewChrome.Ys);

        vm.FormulaSource = "mean(VDC)";

        Assert.Equal(string.Empty, vm.FormulaSource);
        var metric = Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram.Measure)).Metric;
        Assert.IsType<MeasureSource>(metric.Source);
        Assert.Equal("VDC", metric.ChannelKey);
    }

    [Fact]
    public void Formula_ident_change_refreshes_preview_from_sibling()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("formula-ident");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.BandScalar);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        Assert.Equal("mean(VDC)", vm.FormulaSource);
        Assert.NotEmpty(vm.Preview.CannedSamples);

        vm.FormulaSource = "mean(rail.mean)";

        Assert.True(string.IsNullOrWhiteSpace(vm.FormulaError), vm.FormulaError);
        Assert.NotEmpty(vm.Preview.CannedSamples);
        var expr = Assert.IsType<ExpressionAlgorithm>(vm.SelectedMetric!.Source);
        Assert.Contains("rail.mean", expr.InputChannelKeys);
        Assert.DoesNotContain("VDC", expr.InputChannelKeys);
    }

    [Fact]
    public void Selecting_recording_drives_formula_preview_from_samples()
    {
        var root = EmptyWorkspace();
        var dest = Path.Combine(root, "recordings", "sample", "mean-vdc");
        Directory.CreateDirectory(dest);
        File.Copy(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json"),
            Path.Combine(dest, "run.json"));

        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("sample");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        Assert.Contains("mean-vdc-1", vm.DatasetItems);
        Assert.NotNull(vm.SelectedDataset);
        Assert.Equal(2, vm.Preview.CannedValue);
        Assert.Contains("Recording", vm.PreviewNote, StringComparison.Ordinal);
        Assert.DoesNotContain("needs Area 11 filter", vm.PreviewNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Transfer_function_recipe_preview_uses_synthesized_clock()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("tf-ui");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
        Assert.Equal("VDC.filt", vm.ChannelKey);
        Assert.Equal("0.5 0.5", vm.TfNumerator);
        Assert.Equal("filter", vm.TfMethod);
        Assert.NotEmpty(vm.Preview.CannedSamples);
        Assert.DoesNotContain("TF_GRID", vm.PreviewNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_tf_json_sets_output_channel_key()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("tf-import");
        vm.ImportTransferFunction(
            Path.Combine(FindRepoRoot(), "tests", "fixtures", "authoring", "tf", "model.valid.json"));
        Assert.Equal("VDC.filt", vm.ChannelKey);
        var tf = Assert.IsType<TransferFunctionAlgorithm>(vm.SelectedMetric!.Source);
        Assert.Equal("VDC", tf.InputChannelKey);
        Assert.Equal([0.5, 0.5], tf.Numerator);
    }

    private static string GetSetting(MetricDraft metric, string key)
        => metric.Source switch
        {
            AlgorithmSource algorithm when algorithm.Settings.TryGetValue(key, out var value) => value,
            MeasureSource measure when measure.Settings.TryGetValue(key, out var value) => value,
            _ => string.Empty,
        };

    private static string HangForeverXml()
    {
        AuthoringPluginSearch.Search();
        var hang = new HangForeverStep { Name = "Hang Forever" };
        var tmp = Path.Combine(Path.GetTempPath(), "ht-hang-" + Guid.NewGuid().ToString("N") + ".TapPlan");
        var source = new TestPlan();
        source.ChildTestSteps.Add(hang);
        source.Save(tmp);
        try
        {
            return System.Xml.Linq.XDocument.Load(tmp)
                .Descendants()
                .First(e => e.Name.LocalName == "TestStep")
                .ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }
        finally
        {
            File.Delete(tmp);
        }
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
