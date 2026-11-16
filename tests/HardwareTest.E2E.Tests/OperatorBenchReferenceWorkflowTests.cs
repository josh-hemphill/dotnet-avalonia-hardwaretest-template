using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorBenchReferenceWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Golden_acquisition_promotion_selection_and_return_focus_use_real_views_and_saved_evidence(bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        var oldTheme = app.RequestedThemeVariant;
        var oldEngineer = app.SettingsStore.AppSettings.IsEngineerDebugMode;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        app.SettingsStore.AppSettings.IsEngineerDebugMode = true;
        await app.SettingsStore.SaveAppSettingsAsync();
        using var shown = E2EHarness.ShowMainWindow();
        using var fixture = new ReferencePlanFixture();
        var window = shown.Window; window.Width = width; window.Height = height; window.FontSize = font;
        var main = shown.Main; var run = main.RunTest; main.IsNavPaneOpen = false;
        var typography = new List<(string Name, double Box, double Presenter)>();
        try
        {
            main.NavigateToPageId("RunTest");
            await run.ProgramSelection.RefreshProgramsAsync();
            run.ProgramSelection.SelectedProgram = run.ProgramSelection.Programs.Single(program => program.Id == fixture.Id);
            await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady && run.References.Rules.Count == 1 && !run.References.IsBusy);
            Assert.False(run.References.IsReady);
            var open = Named<Button>(window, "Golden references");
            OperatorWorkflowUiTests.Click(open);
            var dialog = await Dialog(window, "Golden references");
            Capture(dialog, "golden-reference-missing", dark, width, height, font);
            var acquisition = Named<CheckBox>(dialog, "Reference acquisition mode");
            acquisition.BringIntoView(); Settle(dialog);
            Assert.True(acquisition.Focus());
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(dialog); Assert.True(run.References.IsAcquisition);
            Capture(dialog, "golden-reference-acquisition", dark, width, height, font);
            Close(dialog); Settle(window); Assert.True(open.IsFocused);
            run.SessionPanel.DutSerialInput = "GOLDEN-UI-DUT";
            run.SessionPanel.OperatorInput = "Golden reference technician";
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.True(run.CanStartRun);
            var execution = run.Run.RunCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator)
                {
                    foreach (var field in run.Interaction.InteractionFields)
                    {
                        if (field.IsBoolean) field.BoolValue = true;
                        else if (string.IsNullOrWhiteSpace(field.Value)) field.Value = field.IsNumber ? "0" : "Golden fixture";
                    }
                    run.ContinueOperatorAttention();
                }
                return execution.IsCompleted && !run.IsRunning;
            }, TimeSpan.FromMinutes(2));
            await execution;
            var runs = app.Services.GetRequiredService<IRunStore>();
            var source = Assert.IsType<TestRunRecord>(await runs.LoadAsync(run.LastRunId!));
            Assert.Equal(RunExecutionScopes.ReferenceAcquisition, source.ExecutionScope);
            Assert.Equal(RunResult.Passed, source.Result);
            var sourcePath = Path.Combine(runs.GetRunDirectory(source.RunId), "run.json");
            var originalBytes = File.ReadAllBytes(sourcePath);
            Capture(window, "golden-reference-acquired", dark, width, height, font);

            OperatorWorkflowUiTests.Click(Named<Button>(window, "View saved result"));
            await E2EHarness.WaitUntilAsync(() => main.Results.OpenedRun?.RunId == source.RunId);
            var promote = Named<Button>(window, "Save golden reference from this run");
            OperatorWorkflowUiTests.Click(promote);
            var saveDialog = await Dialog(window, "Save golden reference");
            RecordReferenceTextFont(typography, Named<TextBox>(saveDialog, "Golden reference name"));
            RecordReferenceTextFont(typography, Named<TextBox>(saveDialog, "Golden reference validity in days"));
            var save = Named<Button>(saveDialog, "Save new golden reference");
            OperatorWorkflowUiTests.Inside(save, saveDialog);
            OperatorWorkflowUiTests.Click(save);
            Assert.Contains("Enter a name", main.Results.ReferencePromotion.Status, StringComparison.Ordinal);
            Assert.True(Named<TextBox>(saveDialog, "Golden reference name").IsFocused);
            Capture(saveDialog, "golden-reference-name-required", dark, width, height, font);
            Type(saveDialog, Named<TextBox>(saveDialog, "Golden reference name"), "Fixture golden");
            Type(saveDialog, Named<TextBox>(saveDialog, "Golden reference validity in days"), "not a duration");
            OperatorWorkflowUiTests.Click(save);
            Assert.Contains("Validity", main.Results.ReferencePromotion.Status, StringComparison.Ordinal);
            Assert.True(Named<TextBox>(saveDialog, "Golden reference validity in days").IsFocused);
            Capture(saveDialog, "golden-reference-validity-required", dark, width, height, font);
            Type(saveDialog, Named<TextBox>(saveDialog, "Golden reference validity in days"), "30");
            OperatorWorkflowUiTests.Click(save);
            await E2EHarness.WaitUntilAsync(() => !main.Results.ReferencePromotion.IsSaving
                && main.Results.ReferencePromotion.Status.StartsWith("Saved", StringComparison.Ordinal));
            Capture(saveDialog, "golden-reference-saved", dark, width, height, font);
            Close(saveDialog); Settle(window); Assert.True(promote.IsFocused);
            var store = app.Services.GetRequiredService<IBenchReferenceStore>();
            var original = Assert.Single(await store.ListAsync(), entry => entry.Profile?.Context.PlanId == fixture.Id).Profile!;
            var duplicate = BenchReferenceEvaluator.Capture("Fixture golden", source, source.ReferenceRules, DateTimeOffset.UtcNow);
            await store.SaveAsync(duplicate);

            main.NavigateToPageId("RunTest");
            await run.RefreshBenchReferencesAsync();
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Golden references"));
            dialog = await Dialog(window, "Golden references");
            acquisition = Named<CheckBox>(dialog, "Reference acquisition mode");
            acquisition.BringIntoView(); Settle(dialog); Assert.True(acquisition.Focus());
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Settle(dialog); Assert.False(run.References.IsAcquisition);
            var combo = Named<ComboBox>(dialog, "Reference for VDC — nominal-voltage (V), rule voltage-reference");
            combo.BringIntoView(); Settle(dialog); Assert.True(combo.Focus());
            dialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            dialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Settle(dialog);
            Assert.NotNull(run.References.Rules.Single().SelectedChoice);
            Assert.Contains(run.References.Rules.Single().SelectedChoice!.Profile.Id, new[] { original.Id, duplicate.Id });
            Assert.Equal(2, combo.ItemCount);
            var selectedLabel = Assert.Single(combo.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == run.References.Rules.Single().SelectedChoice!.Label);
            var choiceTypography = $"Reference ComboBox={combo.FontSize:G17}px; selected label={selectedLabel.FontSize:G17}px; owner={dialog.FontSize:G17}px";
            TestContext.Current.TestOutputHelper?.WriteLine(choiceTypography);
            Assert.True(run.SessionPanel.SessionBlocked);
            OperatorWorkflowUiTests.Inside(Named<Button>(dialog, "Save reference choices"), dialog);
            OperatorWorkflowUiTests.Click(Named<Button>(dialog, "Save reference choices"));
            Capture(dialog, "golden-reference-selected", dark, width, height, font);
            Close(dialog); Settle(window);
            Assert.True(Named<Button>(window, "Golden references").IsFocused);
            Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
            Assert.True(typography.All(value => Math.Abs(value.Box - font) < 0.01 && Math.Abs(value.Presenter - font) < 0.01),
                $"Expected golden-save text fields and presenters to equal {font}px. "
                + string.Join("; ", typography.Select(value => $"{value.Name}: TextBox={value.Box:G17}px, PART_TextPresenter={value.Presenter:G17}px"))
                + "; " + choiceTypography);
        }
        finally
        {
            foreach (var owned in window.OwnedWindows.ToArray()) owned.Close();
            if (run.CanSafetyStop) { await main.SafetyStopCommand.ExecuteAsync(); await E2EHarness.WaitUntilAsync(() => !run.IsRunning); }
            app.RequestedThemeVariant = oldTheme;
            app.SettingsStore.AppSettings.IsEngineerDebugMode = oldEngineer;
        }
    }

    private static void RecordReferenceTextFont(List<(string Name, double Box, double Presenter)> typography, TextBox textBox)
    {
        var presenters = textBox.GetVisualDescendants().OfType<TextPresenter>().Where(presenter => presenter.Name == "PART_TextPresenter").ToArray();
        Assert.True(presenters.Length == 1, $"{AutomationProperties.GetName(textBox)} must have one realized PART_TextPresenter; found {presenters.Length}.");
        typography.Add((AutomationProperties.GetName(textBox) ?? "Golden reference field", textBox.FontSize, presenters[0].FontSize));
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
    private static async Task<Window> Dialog(Window owner, string title)
    {
        await E2EHarness.WaitUntilAsync(() => owner.OwnedWindows.Any(window => window.Title == title));
        var dialog = owner.OwnedWindows.Single(window => window.Title == title); Settle(dialog); return dialog;
    }
    private static void Close(Window window) { window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible); }
    private static void Type(Window window, TextBox input, string value)
    { input.BringIntoView(); Settle(window); Assert.True(input.Focus()); input.SelectAll(); window.KeyTextInput(value); Settle(window); Assert.Equal(value, input.Text); }
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); using (var frame = window.CaptureRenderedFrame()) Assert.NotNull(frame); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        Settle(window); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "operator-persona-review");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    private sealed class ReferencePlanFixture : IDisposable
    {
        public string Id { get; } = "golden-ui-" + Guid.NewGuid().ToString("N");
        private readonly string _plan;
        private readonly string _sidecar;
        public ReferencePlanFixture()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Programs"); Directory.CreateDirectory(folder);
            _plan = Path.Combine(folder, Id + ".TapPlan"); _sidecar = Path.ChangeExtension(_plan, ".program.json");
            var plan = SampleProgramFactory.Create();
            plan.ChildTestSteps[0].ChildTestSteps.Insert(0, new RequireOperatorReferenceAcceptanceStep());
            var producer = Flatten(plan.ChildTestSteps).OfType<AcquireVoltageStep>().Single();
            plan.Save(_plan);
            File.WriteAllText(_sidecar, JsonSerializer.Serialize(new ProgramSidecar
            {
                DisplayName = "Golden-unit reference review",
                ReferenceRules = [new()
                {
                    RuleId = "voltage-reference", MetricKey = "VDC", ProducerStepId = producer.Id,
                    ReferenceKey = "nominal-voltage", Unit = "V", Tolerance = 0.01,
                }],
            }, ProgramCatalogJsonContext.Default.ProgramSidecar));
        }
        private static IEnumerable<global::OpenTap.ITestStep> Flatten(IEnumerable<global::OpenTap.ITestStep> nodes)
        { foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.ChildTestSteps)) yield return child; } }
        public void Dispose() { File.Delete(_plan); File.Delete(_sidecar); }
    }
}
