using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringMeasureInspectorTests
{
    [Fact]
    public void Acquire_inspector_edits_sample_count_and_function_id()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("acquire-settings");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var acquire = SelectMetric(vm, "VDC");
        Assert.True(vm.HasStepSettings);
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, vm.MetricFunctionId);
        Assert.Contains(AuthoringFunctionIds.BasicBitSweepAcquire, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicMeanGte, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicApplyTransferFunction, vm.MetricFunctionIdOptions);
        Assert.Equal("32", SettingValue(vm, "SampleCount"));
        Assert.Equal("Sample count", SettingRow(vm, "SampleCount").Label);
        Assert.Equal(vm.MetricFunctionIdOptions.Count, vm.MetricFunctionChoices.Count);
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, vm.SelectedMetricFunction?.Id);
        Assert.Contains("Acquire", vm.SelectedMetricFunction?.Title, StringComparison.OrdinalIgnoreCase);

        vm.SetMetricSetting("SampleCount", "64");
        vm.SelectedMetricFunction = vm.MetricFunctionChoices.First(choice =>
            choice.Id == AuthoringFunctionIds.BasicBitSweepAcquire);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte;

        Assert.Equal("64", SettingValue(vm, "SampleCount"));
        Assert.Equal(AuthoringFunctionIds.BasicBitSweepAcquire, vm.MetricFunctionId);
        Assert.Equal(acquire.Key, vm.SelectedSequence?.Key);
        var measure = Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source);
        Assert.Equal("64", measure.Settings["SampleCount"]);
    }

    [Fact]
    public void Mean_gte_inspector_switches_algorithm_id()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("mean-function");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        SelectMetric(vm, "VDC.mean");
        Assert.True(vm.HasStepSettings);
        Assert.Equal(AuthoringFunctionIds.BasicMeanGte, vm.MetricFunctionId);
        Assert.Contains(AuthoringFunctionIds.BasicPublishBandScalar, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicAcquireVoltage, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicApplyTransferFunction, vm.MetricFunctionIdOptions);

        vm.MetricFunctionId = AuthoringFunctionIds.BasicPublishBandScalar;
        vm.MetricFunctionId = AuthoringFunctionIds.BasicAcquireVoltage;
        vm.MetricFunctionId = AuthoringFunctionIds.BasicApplyTransferFunction;

        Assert.Equal(AuthoringFunctionIds.BasicPublishBandScalar, vm.MetricFunctionId);
        var algorithm = Assert.IsType<AlgorithmSource>(vm.SelectedMetric!.Source);
        Assert.Equal(AuthoringFunctionIds.BasicPublishBandScalar, algorithm.AlgorithmId);
        Assert.Equal("8", algorithm.Settings["SampleCount"]);
    }

    [Fact]
    public void Formula_and_identity_hide_step_settings()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("settings-gate");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        var formula = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC.mean", StringComparison.Ordinal));
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(formula));
        Assert.True(vm.HasFormula);
        Assert.False(vm.HasStepSettings);
        Assert.Empty(vm.MetricFunctionIdOptions);
        vm.SetMetricSetting("SampleCount", "99");
        vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte;
        Assert.IsType<ExpressionAlgorithm>(vm.SelectedMetric!.Source);

        vm.ApplyRecipe(AuthoringRecipeIds.TransferFunction);
        var tf = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC.filt", StringComparison.Ordinal));
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(tf));
        Assert.True(vm.HasTransferFunction);
        Assert.False(vm.HasStepSettings);
        Assert.Empty(vm.MetricFunctionIdOptions);

        var identity = vm.SequenceItems.Single(row => row.Label == "Identity Check");
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(identity));
        Assert.False(vm.HasStepSettings);
        Assert.False(vm.HasMetricPresentation);
        vm.HistoryEnabled = true;
        Assert.False(vm.HistoryEnabled);
    }

    [Fact]
    public void Unknown_function_id_stays_in_options_and_unknown_set_is_ignored()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("unknown-function");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        var source = Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source);
        vm.ReplaceSelected(
            vm.SelectedProgram! with
            {
                Measure = [new MetricNode(vm.SelectedMetric with { Source = source with { FunctionId = "Custom.Unknown" } })],
            },
            rebuildLists: false);
        SelectMetric(vm, "VDC");
        Assert.Equal("Custom.Unknown", vm.MetricFunctionId);
        Assert.Equal("Custom.Unknown", vm.MetricFunctionIdOptions[0]);
        Assert.Equal("Custom.Unknown", vm.SelectedMetricFunction?.Id);
        Assert.Equal("Custom.Unknown", vm.SelectedMetricFunction?.Title);
        vm.MetricFunctionId = "Also.Unknown";
        Assert.Equal("Custom.Unknown", vm.MetricFunctionId);
    }

    [Fact]
    public void History_defaults_to_mixin_enabled_and_empty_watch_does_not_disable()
    {
        var vm = OpenEmpty();
        vm.CreateProgram("history-default");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        Assert.Null(vm.SelectedMetric!.History);
        Assert.True(vm.HistoryEnabled);
        vm.HistoryEnabled = true;
        vm.HistoryWatchPercent = string.Empty;
        vm.HistoryAlertPercent = string.Empty;
        Assert.Null(vm.SelectedMetric.History);
        vm.HistoryWatchPercent = "5";
        Assert.Equal(new HistorySpec(true, 5, null), vm.SelectedMetric.History);
        vm.HistoryEnabled = false;
        Assert.Equal(new HistorySpec(false, 5, null), vm.SelectedMetric.History);
        vm.SetMetricSetting("SeriesCompliance", SeriesComplianceModes.None);
        var series = SettingRow(vm, "SeriesCompliance");
        Assert.Equal("Series compliance", series.Label);
        Assert.False(string.IsNullOrWhiteSpace(series.ValueTooltip));
        Assert.Equal(SeriesComplianceModes.None, series.ValuePlaceholder);
    }

    [Fact]
    public void History_disabled_round_trips_through_save_plan()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("history-off");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        vm.HistoryEnabled = false;
        Assert.Equal(new HistorySpec(false, null, null), vm.SelectedMetric!.History);
        vm.Apply();

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("history-off");
        SelectMetric(reloaded, "VDC");
        Assert.False(reloaded.HistoryEnabled);
        Assert.Equal(new HistorySpec(false, null, null), reloaded.SelectedMetric!.History);
    }

    [Fact]
    public void History_spec_round_trips_through_save_plan()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateProgram("history-inspector");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        vm.SetMetricSetting("SampleCount", "48");
        vm.HistoryEnabled = true;
        vm.HistoryWatchPercent = "5";
        vm.HistoryAlertPercent = "10";
        Assert.True(vm.HistoryEnabled);
        Assert.Equal("5", vm.HistoryWatchPercent);
        Assert.Equal("10", vm.HistoryAlertPercent);

        vm.Apply();

        var reloaded = new AuthoringWorkspaceViewModel();
        reloaded.Open(root);
        reloaded.SelectProgram("history-inspector");
        SelectMetric(reloaded, "VDC");
        Assert.True(reloaded.HasStepSettings);
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, reloaded.MetricFunctionId);
        Assert.Equal("48", SettingValue(reloaded, "SampleCount"));
        Assert.True(reloaded.HistoryEnabled);
        Assert.Equal("5", reloaded.HistoryWatchPercent);
        Assert.Equal("10", reloaded.HistoryAlertPercent);
        Assert.Equal(new HistorySpec(true, 5, 10), reloaded.SelectedMetric!.History);
    }

    private static SequenceRow SelectMetric(AuthoringWorkspaceViewModel vm, string channelKey)
    {
        var row = vm.SequenceItems.Single(item =>
            item.Kind == SequenceRowKind.Metric
            && item.Detail.Contains(channelKey, StringComparison.Ordinal));
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(row));
        return row;
    }

    private static string SettingValue(AuthoringWorkspaceViewModel vm, string key)
        => SettingRow(vm, key).Value;

    private static AuthoringSettingRow SettingRow(AuthoringWorkspaceViewModel vm, string key)
        => vm.MetricSettingRows.Single(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));

    private static AuthoringWorkspaceViewModel OpenEmpty()
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(EmptyWorkspace());
        return vm;
    }

    private static string EmptyWorkspace()
    {
        var dest = Path.Combine(Path.GetTempPath(), "ht-measure-inspector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        File.Copy(Path.Combine(src, "authoring.json"), Path.Combine(dest, "authoring.json"));
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
