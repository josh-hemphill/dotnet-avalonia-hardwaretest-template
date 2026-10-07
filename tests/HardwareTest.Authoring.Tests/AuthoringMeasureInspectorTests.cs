using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringMeasureInspectorTests
{
    [Fact]
    public void Combo_box_choices_keep_identity_until_their_values_change()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("stable-choices");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");

        var units = vm.YUnitOptions;
        var functions = vm.MetricFunctionChoices;
        var selectedFunction = vm.SelectedMetricFunction;
        Assert.Same(units, vm.YUnitOptions);
        Assert.Same(functions, vm.MetricFunctionChoices);
        Assert.Same(selectedFunction, vm.SelectedMetricFunction);

        vm.YUnit = "custom-unit";
        Assert.NotSame(units, vm.YUnitOptions);
        Assert.Contains("custom-unit", vm.YUnitOptions);

        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.MeanGte);
        SelectMetric(vm, "VDC.mean");
        Assert.NotSame(functions, vm.MetricFunctionChoices);
    }

    [Fact]
    public void Acquire_inspector_edits_sample_count_and_function_id()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("acquire-settings");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        var acquire = SelectMetric(vm, "VDC");
        Assert.True(vm.HasStepSettings);
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, vm.MetricFunctionId);
        Assert.Contains(AuthoringFunctionIds.BasicBitSweepAcquire, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicMeanGte, vm.MetricFunctionIdOptions);
        Assert.DoesNotContain(AuthoringFunctionIds.BasicApplyTransferFunction, vm.MetricFunctionIdOptions);
        Assert.Equal("32", SettingValue(vm, "SampleCount"));
        Assert.Equal("Sample count", SettingRow(vm, "SampleCount").Label);
        Assert.Equal(AuthoringSettingKind.Integer, SettingRow(vm, "SampleCount").Kind);
        Assert.DoesNotContain(vm.MetricSettingRows, row => row.Key == "Channel");
        Assert.Equal(vm.MetricFunctionIdOptions.Count, vm.MetricFunctionChoices.Count);
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, vm.SelectedMetricFunction?.Id);
        Assert.Contains("Acquire", vm.SelectedMetricFunction?.Title, StringComparison.OrdinalIgnoreCase);

        vm.SetMetricSetting("SampleCount", "64");
        vm.SelectedMetricFunction = vm.MetricFunctionChoices.First(choice =>
            choice.Id == AuthoringFunctionIds.BasicBitSweepAcquire);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte;

        Assert.Equal("4", SettingValue(vm, "BitCount"));
        Assert.Equal(AuthoringFunctionIds.BasicBitSweepAcquire, vm.MetricFunctionId);
        Assert.Equal(acquire.Key, vm.SelectedSequence?.Key);
        var measure = Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source);
        Assert.False(measure.Settings.ContainsKey("SampleCount"));
        Assert.Equal("4", measure.Settings["BitCount"]);
    }

    [Fact]
    public void Mean_gte_inspector_switches_algorithm_id()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("mean-function");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.MeanGte);
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
        Assert.False(algorithm.Settings.ContainsKey("SampleCount"));
        Assert.True(algorithm.Settings.ContainsKey("Value"));
    }

    [Fact]
    public void Formula_and_identity_hide_step_settings()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("settings-gate");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
        var formula = vm.SequenceItems.Single(row =>
            row.Kind == SequenceRowKind.Metric && row.Detail.Contains("VDC.mean", StringComparison.Ordinal));
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(formula));
        Assert.True(vm.HasFormula);
        Assert.False(vm.HasStepSettings);
        Assert.Empty(vm.MetricFunctionIdOptions);
        vm.SetMetricSetting("SampleCount", "99");
        vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte;
        Assert.IsType<ExpressionAlgorithm>(vm.SelectedMetric!.Source);

        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.TransferFunction);
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
        vm.CreateDemoProgram("unknown-function");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
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
    public void Clearing_incomplete_history_text_preserves_omitted_history_and_undo_restores_text()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("history-incomplete");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        vm.Apply();
        vm.HistoryWatchPercent = "1e-";
        Assert.Equal("1e-", vm.HistoryWatchPercent);
        Assert.Null(vm.SelectedMetric!.History);
        Assert.True(vm.HasUnsavedChanges);
        vm.HistoryWatchPercent = string.Empty;
        Assert.Null(vm.SelectedMetric.History);
        Assert.False(vm.HasUnsavedChanges);
        vm.Undo();
        Assert.Equal("1e-", vm.HistoryWatchPercent);
        Assert.Null(vm.SelectedMetric.History);
        vm.StopRecovery();
    }

    [Fact]
    public void History_defaults_to_mixin_enabled_and_empty_watch_does_not_disable()
    {
        var vm = OpenEmpty();
        vm.CreateDemoProgram("history-default");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
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
        Assert.Equal(AuthoringSettingKind.Choice, series.Kind);
        Assert.False(string.IsNullOrWhiteSpace(series.ValueTooltip));
        Assert.Equal(SeriesComplianceModes.None, series.ValuePlaceholder);
        vm.SetMetricSetting("FailWhenOutOfBand", "false");
        vm.SetMetricSettingBool("FailWhenOutOfBand", true);
        Assert.Equal("true", SettingValue(vm, "FailWhenOutOfBand"));
        Assert.Equal(AuthoringSettingKind.Boolean, SettingRow(vm, "FailWhenOutOfBand").Kind);
        vm.SetMetricSettingNumber("SampleCount", 48);
        Assert.Equal("48", SettingValue(vm, "SampleCount"));
    }

    [Fact]
    public void History_disabled_round_trips_through_save_plan()
    {
        var root = EmptyWorkspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.CreateDemoProgram("history-off");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
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
        vm.CreateDemoProgram("history-inspector");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
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

    [Theory]
    [InlineData("1e-")]
    [InlineData("")]
    [InlineData("0")]
    public void Incomplete_setting_text_saves_reopens_and_corrects_in_one_edit(string text)
    {
        var root = EmptyWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Package.Name = "Durable setting fixture";
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var vm = new AuthoringWorkspaceViewModel();
        var reopened = new AuthoringWorkspaceViewModel();
        try
        {
            vm.Open(root); vm.CreateDemoProgram("durable-setting"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
            SelectMetric(vm, "VDC");
            var nodeId = vm.SelectedSequence!.NodeId;
            var rows = vm.MetricSettingRows;
            vm.SetMetricSetting("SampleCount", text);
            Assert.Same(rows, vm.MetricSettingRows);
            Assert.Equal(text, SettingValue(vm, "SampleCount"));
            Assert.Equal("32", Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source).Settings["SampleCount"]);
            vm.SaveProgram("durable-setting"); vm.StopRecovery();
            reopened.Open(root); reopened.SelectProgram("durable-setting");
            reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == nodeId));
            Assert.Equal(text, SettingValue(reopened, "SampleCount"));
            Assert.False(reopened.CanPack);
            var revision = reopened.SelectedDocument!.Revision;
            reopened.SetMetricSetting("SampleCount", "64");
            Assert.Equal(revision + 1, reopened.SelectedDocument.Revision);
            Assert.Empty(reopened.SelectedProgram!.AuthoringState.IncompleteNumericText);
            reopened.Undo(); Assert.Equal(text, SettingValue(reopened, "SampleCount"));
            Assert.Equal("32", Assert.IsType<MeasureSource>(reopened.SelectedMetric!.Source).Settings["SampleCount"]);
        }
        finally { vm.StopRecovery(); reopened.StopRecovery(); }
    }

    [Fact]
    public void Raw_step_keeps_implementation_details_without_metric_fields()
    {
        var vm = OpenEmpty(); vm.InitializePlan(new("raw-form") { Instruments = [] });
        vm.ReplaceSelected(vm.SelectedProgram! with { Measure = [new RawStepNode("CustomStep", "<TestStep />")] });
        vm.SelectMeasure(0);
        Assert.True(vm.HasRawStep);
        Assert.False(vm.HasMetricPresentation);
        Assert.False(vm.HasStepSettings);
        Assert.Equal("CustomStep", vm.RawTypeName);
        Assert.Equal("<TestStep />", vm.RawXml);
        Assert.Contains(vm.SelectedSequence!.NodeId!.Value.ToString("D"), vm.SelectedNodeIdentity);
        vm.StopRecovery();
    }

    [Fact]
    public void Binding_guards_reject_missing_and_unsupported_instruments_without_retargeting()
    {
        var vm = OpenEmpty(); vm.CreateDemoProgram("binding-guard"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.ReplaceSelected(vm.SelectedProgram! with { Instruments = [.. vm.SelectedProgram!.Instruments, new InstrumentRef("OPAQUE", "Unknown.Type", "unknown")] });
        SelectMetric(vm, "VDC");
        vm.MetricInstrumentSlot = "missing";
        Assert.Equal("DMM", vm.MetricInstrumentSlot);
        Assert.Contains("existing instrument slot", vm.Error);
        vm.MetricInstrumentSlot = "OPAQUE";
        Assert.Equal("DMM", vm.MetricInstrumentSlot);
        Assert.Contains("INSTRUMENT_UNAVAILABLE", vm.Error);
        var source = Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source);
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [new MetricNode(vm.SelectedMetric with { Source = source with { InstrumentSlot = "OPAQUE" } })] });
        vm.SelectMeasure(0);
        Assert.Contains("INSTRUMENT_UNAVAILABLE", vm.SelectedStepErrors);
        vm.StopRecovery();
    }

    [Fact]
    public void Recipe_migration_retains_compatible_incomplete_text_and_undo_restores_removed_fields()
    {
        var vm = OpenEmpty(); vm.CreateDemoProgram("migration-text"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        SelectMetric(vm, "VDC");
        vm.SetMetricSetting("SampleCount", "1e-"); vm.SetMetricSetting("IntervalMs", "-");
        var nodeId = vm.SelectedSequence!.NodeId;
        vm.MetricFunctionId = AuthoringFunctionIds.BasicBitSweepAcquire;
        Assert.Equal(nodeId, vm.SelectedSequence!.NodeId);
        Assert.Equal("-", SettingValue(vm, "IntervalMs"));
        Assert.DoesNotContain(vm.SelectedProgram!.AuthoringState.IncompleteNumericText.Keys, key => key.EndsWith("MetricSetting:SampleCount", StringComparison.Ordinal));
        vm.Undo(); Assert.Equal("1e-", SettingValue(vm, "SampleCount"));
        vm.Redo(); Assert.Equal("-", SettingValue(vm, "IntervalMs"));
        vm.StopRecovery();
    }

    [Fact]
    public void Algorithm_inputs_edit_the_source_without_changing_display_or_criteria()
    {
        var vm = OpenEmpty(); vm.CreateDemoProgram("inputs"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.MeanGte);
        SelectMetric(vm, "VDC.mean");
        var limits = vm.SelectedMetric!.Limits; var unit = vm.YUnit;
        Assert.False(vm.HasMetricInputs);
        vm.MetricInputChannels = "rail.x, rail.y";
        Assert.Empty(Assert.IsType<AlgorithmSource>(vm.SelectedMetric.Source).InputChannelKeys);
        Assert.Equal(limits, vm.SelectedMetric.Limits); Assert.Equal(unit, vm.YUnit);
        vm.StopRecovery();
    }

    [Fact]
    public void Instrument_free_recipe_migrates_binding_compiles_and_undo_restores_explicit_resource()
    {
        var root = EmptyWorkspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        try
        {
            vm.CreateDemoProgram("binding-migration"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire); SelectMetric(vm, "VDC");
            var nodeId = vm.SelectedSequence!.NodeId;
            vm.MetricFunctionId = AuthoringFunctionIds.BasicPublishTimedSample;
            Assert.False(vm.NeedsMetricInstrument);
            Assert.Empty(Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source).InstrumentSlot);
            Assert.Empty(vm.SelectedStepErrors);
            var path = Path.Combine(root, "binding-migration.tapplan"); var compiler = new PlanCompiler();
            compiler.Save(vm.SelectedProgram!, path);
            var loaded = compiler.Load(path);
            var loadedNode = Assert.IsType<MetricNode>(Assert.Single(loaded.Measure));
            Assert.Equal(nodeId, loadedNode.NodeId);
            Assert.Empty(Assert.IsType<MeasureSource>(loadedNode.Metric.Source).InstrumentSlot);
            vm.Undo(); Assert.Equal("DMM", vm.MetricInstrumentSlot);
            vm.Redo(); Assert.Empty(vm.MetricInstrumentSlot);
            vm.MetricFunctionId = AuthoringFunctionIds.BasicAcquireVoltage;
            Assert.True(vm.NeedsMetricInstrument); Assert.Empty(vm.MetricInstrumentSlot);
            Assert.Contains("Choose an existing instrument slot", vm.SelectedStepErrors);
            Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(vm.SelectedProgram!, path));
            vm.MetricInstrumentSlot = "DMM";
            compiler.Save(vm.SelectedProgram!, path);
            Assert.Equal("DMM", Assert.IsType<MeasureSource>(Assert.IsType<MetricNode>(Assert.Single(compiler.Load(path).Measure)).Metric.Source).InstrumentSlot);
        }
        finally { vm.StopRecovery(); }
    }

    [Fact]
    public void Channel_average_authoritative_input_and_display_survive_compilation_and_recipe_history()
    {
        var root = EmptyWorkspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        try
        {
            vm.CreateDemoProgram("consumed-input"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.MeanGte);
            SelectMetric(vm, "VDC.mean");
            vm.MetricFunctionId = AuthoringFunctionIds.BasicChannelAverage;
            Assert.True(vm.HasMetricInputs); Assert.Empty(vm.MetricInputChannels);
            Assert.Null(Assert.IsType<AlgorithmSource>(vm.SelectedMetric!.Source).InstrumentSlot);
            Assert.Contains("exactly one", vm.SelectedStepErrors);
            vm.MetricInputChannels = "VDC"; vm.YUnit = "mV";
            Assert.Empty(vm.SelectedStepErrors);
            foreach (var alias in new[] { "InputChannel", "ProducerStepId", "Channel", "Unit" })
                Assert.DoesNotContain(vm.MetricSettingRows, row => row.Key == alias);
            var compiler = new PlanCompiler(); var path = Path.Combine(root, "consumed-input.tapplan");
            compiler.Save(vm.SelectedProgram!, path);
            var loadedMetric = Assert.IsType<MetricNode>(compiler.Load(path).Measure[1]).Metric;
            Assert.Equal(new[] { "VDC" }, Assert.IsType<AlgorithmSource>(loadedMetric.Source).InputChannelKeys);
            Assert.Equal("mV", loadedMetric.YUnit);
            Assert.Equal("mV", Assert.IsType<AlgorithmSource>(loadedMetric.Source).Settings["Unit"]);
            vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte;
            Assert.False(vm.HasMetricInputs);
            Assert.Empty(Assert.IsType<AlgorithmSource>(vm.SelectedMetric!.Source).InputChannelKeys);
            Assert.Null(Assert.IsType<AlgorithmSource>(vm.SelectedMetric.Source).InstrumentSlot);
            Assert.Contains("Choose an existing instrument slot", vm.SelectedStepErrors);
            vm.Undo(); Assert.True(vm.HasMetricInputs); Assert.Equal("VDC", vm.MetricInputChannels);
            vm.Redo(); Assert.False(vm.HasMetricInputs); Assert.Empty(Assert.IsType<AlgorithmSource>(vm.SelectedMetric!.Source).InputChannelKeys);
        }
        finally { vm.StopRecovery(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("VDC, VDC")]
    public void Unsupported_channel_average_cardinality_is_visible_and_cannot_compile(string channels)
    {
        var root = EmptyWorkspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        try
        {
            vm.CreateDemoProgram("input-count"); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire); vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.MeanGte);
            SelectMetric(vm, "VDC.mean"); vm.MetricFunctionId = AuthoringFunctionIds.BasicChannelAverage;
            vm.MetricInputChannels = channels;
            Assert.Contains("exactly one", vm.SelectedStepErrors);
            Assert.Contains(AuthoringIssueService.GetIssues(vm.SelectedProgram!), issue => issue.Code == "INPUT_CHANNEL_CARDINALITY");
            var error = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler().Save(vm.SelectedProgram!, Path.Combine(root, "input-count.tapplan")));
            Assert.Contains("exactly one", error.Message);
        }
        finally { vm.StopRecovery(); }
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
            if (dir.EnumerateFiles("dirs.proj").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate dirs.proj above '{AppContext.BaseDirectory}'.");
    }
}
