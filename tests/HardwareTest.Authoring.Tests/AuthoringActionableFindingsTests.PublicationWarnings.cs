using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed partial class AuthoringActionableFindingsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Completed_save_retains_a_verification_warning_when_inputs_become_unreadable_after_publication(bool empty, bool all)
    {
        await PrepareCheckedInputsAsync(empty);
        Assert.Contains("Current", _vm.IssuesCheckState);
        if (all) _vm.DisplayName = "saved with verification warning";
        var sourcePath = CheckedInputPath("program");
        var sourceBefore = File.ReadAllBytes(sourcePath);
        var manifestPath = CheckedInputPath("manifest");
        var manifestBefore = File.ReadAllBytes(manifestPath);
        FileStream? held = null;
        void OnPublished(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(_vm.Status) && _vm.Status?.StartsWith("Saved sample.", StringComparison.Ordinal) == true)
            {
                if (all) _vm.Validate();
                Assert.Contains("Current", _vm.IssuesCheckState);
                held ??= new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
        }
        _vm.PropertyChanged += OnPublished;
        try
        {
            if (all)
            {
                var result = _vm.SaveAll();
                Assert.True(result.Succeeded);
                Assert.Equal(["sample"], result.SavedProgramIds);
                Assert.Empty(result.Failures);
            }
            else _vm.SaveSidecar();
            Assert.NotNull(held);
            Assert.False(_vm.HasUnsavedChanges);
            AssertCheckedStateStale();
            Assert.Contains("could not be read to verify", _vm.Error);
        }
        finally
        {
            _vm.PropertyChanged -= OnPublished;
            held?.Dispose();
        }
        Assert.False(sourceBefore.SequenceEqual(File.ReadAllBytes(sourcePath)));
        Assert.Equal(manifestBefore, File.ReadAllBytes(manifestPath));
    }

    [Fact]
    public void Unsupported_filter_limits_issue_advertises_and_navigates_to_program_settings()
    {
        _vm.SelectProgram("sample");
        _vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Formula);
        var formula = Assert.IsType<MetricNode>(_vm.SelectedProgram!.Measure.Last());
        _vm.ReplaceSelected(_vm.SelectedProgram with
        {
            Measure = [.. _vm.SelectedProgram.Measure.Take(_vm.SelectedProgram.Measure.Count - 1), formula with
            {
                Metric = formula.Metric with { Source = new ExpressionAlgorithm(["VDC"], "filter([1],[1],VDC)"), Limits = new LimitSpec(2, 1, null) }
            }]
        });
        _vm.SelectMeasure(_vm.SelectedProgram.Measure.Count - 1);
        Assert.False(_vm.ShowThreshold);
        var issue = Assert.Single(_vm.EditingIssues, item => item.Code == AuthoringCompileCodes.MissingLimits);
        Assert.Equal("Open program settings", issue.NavigationLabel);
        Assert.Null(issue.Field);
        var program = _vm.SelectedProgram.PlanId;
        _vm.InitializePlan(new("other") { Instruments = [] });
        var target = _vm.NavigateEditingIssue(issue);
        Assert.Equal(program, _vm.SelectedProgram!.PlanId);
        Assert.Null(target!.NodeId);
        Assert.Null(target.Field);
        Assert.Equal("ProgramSettings", target.Section);
        Assert.Null(_vm.Error);
    }
}
