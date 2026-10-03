using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringDestructiveReviewRegressionTests
{
    [AvaloniaTheory]
    [InlineData("sidecar")]
    [InlineData("settings")]
    [InlineData("raw")]
    public void Actual_program_removal_dialog_rejects_mutable_content_changes_with_the_same_selected_draft(string change)
    {
        var compiler = new MutableProgramCompiler(); using var fixture = Loaded(compiler); var selected = fixture.ViewModel.SelectedProgram;
        var files = Snapshot(fixture); AuthoringUiFixture.Click(fixture.Control<Button>("Remove program")); var dialog = Assert.Single(fixture.Window!.OwnedWindows);
        switch (change)
        {
            case "sidecar": selected!.Sidecar.DisplayName = "changed while reviewing"; break;
            case "settings": compiler.Settings["Samples"] = "3"; break;
            default: compiler.Nodes[1] = Assert.IsType<RawStepNode>(compiler.Nodes[1]) with { XmlFragment = "<changed/>" }; break;
        }
        Assert.Same(selected, fixture.ViewModel.SelectedProgram);
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove program", dialog)); Assert.Empty(fixture.Window!.OwnedWindows);
        Assert.Contains("changed since review", fixture.ViewModel.Error); Assert.Contains("prepare and review a new impact", fixture.ViewModel.Error);
        Assert.Same(selected, fixture.ViewModel.SelectedProgram); AssertFiles(files);
    }

    [AvaloniaFact]
    public void Actual_instrument_dialog_rejects_nested_settings_changed_during_review()
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateProgram("slots"); vm.NewInstrumentSlot = "B"; vm.AddInstrumentSlot(); vm.ApplyRecipe(AuthoringRecipeIds.Acquire); vm.ApplyRecipe(AuthoringRecipeIds.Repeat); vm.SelectedInstrumentSlot = "DMM"; Assert.True(vm.SaveAll().Succeeded);
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1; AuthoringUiFixture.Drain();
        var remove = fixture.Control<Button>("Remove instrument slot from selected program"); remove.BringIntoView(); AuthoringUiFixture.Drain(); AuthoringUiFixture.Click(remove);
        var dialog = Assert.Single(fixture.Window!.OwnedWindows); var selected = vm.SelectedProgram; var files = Snapshot(fixture);
        var metric = Assert.IsType<MetricNode>(Assert.Single(Assert.IsType<RepeatNode>(Assert.Single(selected!.Measure)).Children));
        Assert.IsAssignableFrom<IDictionary<string, string>>(Assert.IsType<MeasureSource>(metric.Metric.Source).Settings)["Samples"] = "999";
        fixture.Control<ComboBox>("Compatible replacement instrument slot", dialog).SelectedItem = "B"; AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Remove and replace slot", dialog)); Assert.Contains("changed since review", vm.Error);
        Assert.Same(selected, vm.SelectedProgram); Assert.Equal(["DMM", "B"], vm.InstrumentSlots); AssertFiles(files);
    }

    [AvaloniaTheory]
    [InlineData("measure")]
    [InlineData("membership")]
    public void Actual_cleanup_checkboxes_clone_selected_sidecar_preserve_prior_draft_and_stage_files(string operation)
    {
        using var fixture = Loaded(); var vm = fixture.ViewModel; vm.CreateProgram("a"); vm.CreateProgram("b"); Assert.True(vm.SaveAll().Succeeded);
        var other = vm.SelectedProgram; vm.SelectProgram("a"); SelectCleanup(fixture); var prior = vm.SelectedProgram!;
        var before = JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar); var files = Snapshot(fixture);
        var box = operation == "measure" ? fixture.Control<CheckBox>("Include measure slots in cleanup") : fixture.Control<CheckBox>("DMM", fixture.Control<ItemsControl>("Cleanup instrument slots"));
        box.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(box.Focus()); fixture.Window!.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); fixture.Window!.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); AuthoringUiFixture.Drain();
        Assert.NotSame(prior.Sidecar, vm.SelectedProgram!.Sidecar); Assert.Equal(before, JsonSerializer.Serialize(prior.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
        Assert.Equal(["DMM"], prior.Cleanup.InstrumentSlots); Assert.Same(other, vm.Programs.Single(p => p.PlanId == "b")); Assert.Equal(new DirtyProgramSummary("a", true, true), Assert.Single(vm.DirtyPrograms)); AssertFiles(files);
        if (operation == "measure") Assert.True(vm.SelectedProgram.Cleanup.IncludeMeasureSlots); else Assert.Empty(vm.SelectedProgram.Cleanup.InstrumentSlots);
    }

    [AvaloniaFact]
    public void Actual_cleanup_inspector_is_readable_but_disables_all_edit_controls_for_readonly_workspace()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true, compiler: new MutableProgramCompiler());
        var manifest = Path.Combine(fixture.WorkspaceRoot, "authoring.json"); File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999", StringComparison.Ordinal));
        fixture.Show(); fixture.OpenRememberedWorkspace(); SelectCleanup(fixture); var selected = fixture.ViewModel.SelectedProgram; var files = Snapshot(fixture);
        var shutdown = fixture.Control<CheckBox>("Include Safe Shutdown"); var measure = fixture.Control<CheckBox>("Include measure slots in cleanup"); var membership = fixture.Control<CheckBox>("DMM", fixture.Control<ItemsControl>("Cleanup instrument slots"));
        foreach (var box in new[] { shutdown, measure, membership })
        {
            Assert.True(box.IsEffectivelyVisible); Assert.False(box.IsEffectivelyEnabled); var before = box.IsChecked; box.BringIntoView(); AuthoringUiFixture.Drain();
            var point = box.TranslatePoint(new Point(10, 10), fixture.Window!); Assert.NotNull(point);
            fixture.Window!.MouseDown(point.Value, MouseButton.Left); fixture.Window!.MouseUp(point.Value, MouseButton.Left); AuthoringUiFixture.Drain(); Assert.Equal(before, box.IsChecked);
        }
        Assert.Same(selected, fixture.ViewModel.SelectedProgram); Assert.False(fixture.ViewModel.HasUnsavedChanges); AssertFiles(files);
    }

    private static AuthoringUiFixture Loaded(IPlanCompiler? compiler = null)
    {
        var fixture = new AuthoringUiFixture(rememberWorkspace: true, compiler); fixture.Show(); fixture.OpenRememberedWorkspace(); return fixture;
    }
    private static void SelectCleanup(AuthoringUiFixture fixture)
    {
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Cleanup)); AuthoringUiFixture.Drain();
    }
    private static Dictionary<string, byte[]> Snapshot(AuthoringUiFixture fixture) => Directory.EnumerateFiles(fixture.WorkspaceRoot).ToDictionary(p => p, File.ReadAllBytes);
    private static void AssertFiles(Dictionary<string, byte[]> files) { foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key)); }
    private sealed class MutableProgramCompiler : IPlanCompiler
    {
        public Dictionary<string, string> Settings { get; } = new() { ["Samples"] = "2" };
        public MeasureNode[] Nodes { get; }
        public MutableProgramCompiler()
        {
            Nodes = [new RepeatNode(2, [new MetricNode(new MetricDraft("acquire", "channel", "timeseries", "V", null, null,
                new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, Settings)))]), new RawStepNode("Raw", "<original/>")];
        }
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => new(workspace, [AuthoringRecipeCatalog.CreateProgram("sample") with { Measure = Nodes }]);
        public ProgramDraft Load(string path) => throw new NotSupportedException();
        public void Save(ProgramDraft draft, string path) => throw new NotSupportedException();
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new NotSupportedException();
    }
}
