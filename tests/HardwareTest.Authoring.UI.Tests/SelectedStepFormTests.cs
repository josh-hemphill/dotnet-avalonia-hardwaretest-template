using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class SelectedStepFormTests
{
    [AvaloniaFact]
    public void Configure_display_and_advanced_keep_criteria_and_explicit_second_instrument()
    {
        using var fixture = Open();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("forms");
        vm.NewInstrumentSlot = "SECOND";
        vm.AddInstrumentSlot();
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        AuthoringUiFixture.Drain();
        var limits = vm.SelectedMetric!.Limits;
        var slot = fixture.Control<ComboBox>("Metric instrument slot");
        slot.SelectedItem = "SECOND";
        AuthoringUiFixture.Drain();
        Assert.Equal("SECOND", Assert.IsType<AlgorithmSource>(vm.SelectedMetric.Source).InstrumentSlot);
        fixture.Type(fixture.Control<TextBox>("Friendly step name"), "Operator voltage");
        vm.YUnit = "mV";
        vm.DisplayRole = "timeseries";
        Assert.Equal(limits, vm.SelectedMetric.Limits);
        Assert.Equal("SECOND", vm.MetricInstrumentSlot);
        Assert.Equal("Operator voltage", vm.SelectedMetric.Name);
        Assert.True(fixture.Control<Expander>("Configure selected step").IsEffectivelyVisible);
        Assert.True(fixture.Control<Expander>("Operator display selected step").IsEffectivelyVisible);
        Assert.True(fixture.Control<Expander>("Advanced selected step").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Numeric_setting_text_survives_selection_and_exposes_an_accessible_error()
    {
        using var fixture = Open();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("numbers");
        vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        vm.SelectMeasure(0);
        var id = vm.SelectedSequence!.NodeId;
        AuthoringUiFixture.Drain();
        fixture.Type(fixture.Control<TextBox>("SampleCount"), "1e-");
        Assert.Equal("32", Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source).Settings["SampleCount"]);
        Assert.Equal("1e-", vm.MetricSettingRows.Single(row => row.Key == "SampleCount").Value);
        var errors = fixture.Control<TextBlock>("Selected step errors");
        Assert.True(errors.IsEffectivelyVisible);
        Assert.Contains("MetricSetting:SampleCount", errors.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(errors));
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Setup));
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == id));
        AuthoringUiFixture.Drain();
        Assert.Equal("1e-", fixture.Control<TextBox>("SampleCount").Text);
        vm.SetMetricSetting("SampleCount", "64");
        Assert.DoesNotContain("MetricSetting:SampleCount", vm.SelectedStepErrors);
        Assert.Equal("64", Assert.IsType<MeasureSource>(vm.SelectedMetric!.Source).Settings["SampleCount"]);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicBitSweepAcquire;
        Assert.Contains(vm.MetricSettingRows, row => row.Key == "BitCount");
        Assert.DoesNotContain(vm.MetricSettingRows, row => row.Key == "SampleCount");
        Assert.Contains("compatible settings retained", vm.Status);
        vm.Undo();
        Assert.Equal(AuthoringFunctionIds.BasicAcquireVoltage, vm.MetricFunctionId);
        Assert.Equal("64", vm.MetricSettingRows.Single(row => row.Key == "SampleCount").Value);
        vm.Redo();
        Assert.Equal(AuthoringFunctionIds.BasicBitSweepAcquire, vm.MetricFunctionId);
        Assert.Contains(vm.MetricSettingRows, row => row.Key == "BitCount");
    }

    [AvaloniaFact]
    public void Node_specific_forms_hide_metric_fields_and_keep_incomplete_repeat_text()
    {
        using var fixture = Open();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("node-forms");
        foreach (var recipe in new[] { AuthoringRecipeIds.Prompt, AuthoringRecipeIds.Input, AuthoringRecipeIds.Acquire, AuthoringRecipeIds.Repeat })
            vm.ApplyRecipe(recipe);
        foreach (var (label, field) in new[] { ("Identity Check", "Setup instrument slot"), ("Operator Prompt", "Prompt message"), ("Operator Input", "Input title"), ("Safe Shutdown", "Include Safe Shutdown") })
        {
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Label == label));
            AuthoringUiFixture.Drain();
            Assert.False(vm.HasMetricPresentation);
            Assert.False(fixture.Control<Expander>("Operator display selected step").IsEffectivelyVisible);
            if (field == "Setup instrument slot") Assert.True(fixture.Control<ComboBox>(field).IsEffectivelyVisible);
            else if (field == "Include Safe Shutdown") Assert.True(fixture.Control<CheckBox>(field).IsEffectivelyVisible);
            else Assert.True(fixture.Control<TextBox>(field).IsEffectivelyVisible);
        }
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Repeat));
        AuthoringUiFixture.Drain();
        fixture.Type(fixture.Control<TextBox>("Repeat count"), "-");
        Assert.Equal("-", vm.RepeatCount);
        Assert.Contains("RepeatCount", vm.SelectedStepErrors);
        Assert.False(vm.HasMetricPresentation);
        vm.RepeatCount = "3";
        Assert.Equal(3, vm.SelectedRepeat!.Count);

    }

    [AvaloniaFact]
    public void Forms_follow_consumed_recipe_inputs_and_instrument_requirements()
    {
        using var fixture = Open(); var vm = fixture.ViewModel;
        vm.CreateDemoProgram("recipe-requirements"); vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.SelectMeasure(1); AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<TextBox>("Metric input channels").IsEffectivelyVisible);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicChannelAverage; AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<TextBox>("Metric input channels").IsEffectivelyVisible);
        Assert.False(fixture.Control<ComboBox>("Metric instrument slot").IsEffectivelyVisible);
        fixture.Type(fixture.Control<TextBox>("Metric input channels"), "VDC, VDC");
        Assert.Contains("exactly one", fixture.Control<TextBlock>("Selected step errors").Text);
        fixture.Type(fixture.Control<TextBox>("Metric input channels"), "VDC");
        Assert.Empty(vm.SelectedStepErrors);
        vm.MetricFunctionId = AuthoringFunctionIds.BasicMeanGte; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<TextBox>("Metric input channels").IsEffectivelyVisible);
        Assert.True(fixture.Control<ComboBox>("Metric instrument slot").IsEffectivelyVisible);
        Assert.Contains("Choose an existing instrument slot", fixture.Control<TextBlock>("Selected step errors").Text);
        vm.SelectMeasure(0); vm.MetricFunctionId = AuthoringFunctionIds.BasicPublishTimedSample; AuthoringUiFixture.Drain();
        Assert.False(fixture.Control<ComboBox>("Metric instrument slot").IsEffectivelyVisible);
        Assert.Empty(vm.MetricInstrumentSlot); Assert.Empty(vm.SelectedStepErrors);
    }

    private static AuthoringUiFixture Open()
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        return fixture;
    }
}
