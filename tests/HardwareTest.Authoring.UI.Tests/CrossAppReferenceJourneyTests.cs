using System.Diagnostics;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

/// Opt-in joined evidence. Separate processes share actual persisted artifacts,
/// rather than substituting unrelated authoring and operator fixtures.
public sealed class CrossAppReferenceJourneyTests
{
    private const string PlanId = "cross-app-reference-voltage";

    [AvaloniaFact]
    public async Task Stage1_Author_compile_prepare_bench_files()
    {
        var shared = Environment.GetEnvironmentVariable("HT_CROSS_APP_REVIEW_DIRECTORY");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(shared), "Set HT_CROSS_APP_REVIEW_DIRECTORY for joined application evidence.");
        Assert.True(Path.IsPathFullyQualified(shared!));
        Directory.CreateDirectory(shared!);
        Assert.False(File.Exists(Path.Combine(shared!, "plan-id.txt")), "Use a fresh joined evidence directory for initial authoring.");
        using var fixture = new AuthoringUiFixture();
        var workspace = Path.Combine(shared!, "authoring-workspace");
        Directory.CreateDirectory(workspace);
        foreach (var file in Directory.EnumerateFiles(fixture.WorkspaceRoot))
            File.Copy(file, Path.Combine(workspace, Path.GetFileName(file)));
        var window = fixture.Show(1280, 800);
        var vm = fixture.ViewModel;
        vm.Open(workspace);
        vm.InitializePlan(new PlanInitializationRequest(PlanId)
        {
            StartingPoint = PlanStartingPoint.Empty,
            DisplayName = "Cross-application golden voltage acceptance",
            DeviceFamily = "cross-app-review",
            UseTemplateHardware = false,
            IncludeTemplateMeasurement = false,
            Instruments = [new("DMM", typeof(MockDmmInstrument).FullName!, "MOCK::CROSS-APP")],
            IdentityInstrumentSlot = "DMM",
            RequiredOperatorFields = [RequiredFieldIds.Serial, RequiredFieldIds.Operator],
            IncludeSafeShutdown = true,
        });
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.MetricName = "Measured voltage";
        vm.ChannelKey = "voltage";
        vm.SelectionIncludesCleanup = false;
        fixture.NavigateTask(0);
        await fixture.WaitForReadinessAsync();
        Type(fixture, window, "SampleCount", "12");
        Type(fixture, window, "IntervalMs", "1");
        var require = fixture.Control<CheckBox>("Require golden reference");
        require.BringIntoView(); AuthoringUiFixture.Drain(); Assert.True(require.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        AuthoringUiFixture.Drain();
        Assert.True(vm.RequireGoldenReference);
        Type(fixture, window, "Reference input key", "golden.voltage");
        Type(fixture, window, "Reference tolerance", "0.001");
        Assert.Empty(vm.ReferenceCriterionError);
        Capture(window, shared!, "cross-stage1-authored-reference");
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        await fixture.WaitForReadinessAsync();
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.HasUncompiledSources);
        var path = vm.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == PlanId);
        var loaded = new PlanCompiler().Load(path);
        var rule = Assert.Single(loaded.Sidecar.ReferenceRules!);
        Assert.Equal("voltage", rule.MetricKey);
        Assert.Equal(0.001, rule.Tolerance);
        Assert.Equal("V", rule.Unit);
        Assert.False(loaded.Sidecar.SelectionIncludesCleanup);
        var directory = Path.Combine(shared!, "prepared-programs");
        await PrepareBenchFiles(fixture, directory);
        Assert.NotNull(BenchProgramHandoff.ValidateProgram(Path.Combine(directory, PlanId, PlanId + ".TapPlan")));
        File.WriteAllText(Path.Combine(shared!, "plan-id.txt"), PlanId);
        Capture(fixture.Window!.OwnedWindows.Single(), shared!, "cross-stage1-prepared-receipt");
    }

    [AvaloniaFact]
    public async Task Stage3_Import_exact_operator_recording_edit_recompile()
    {
        var shared = Environment.GetEnvironmentVariable("HT_CROSS_APP_REVIEW_DIRECTORY");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(shared), "Set HT_CROSS_APP_REVIEW_DIRECTORY for joined application evidence.");
        Assert.True(Path.IsPathFullyQualified(shared!));
        Assert.Equal(PlanId, File.ReadAllText(Path.Combine(shared!, "plan-id.txt")));
        var recordingPath = Path.Combine(shared!, "operator-result", "run.json");
        var originalBytes = File.ReadAllBytes(recordingPath);
        var original = JsonSerializer.Deserialize(originalBytes, AppJsonContext.Default.TestRunRecord)!;
        Assert.Equal(PlanId, original.PlanId);
        Assert.NotEmpty(original.ReferenceUses);
        using var fixture = new AuthoringUiFixture();
        var window = fixture.Show(900, 600);
        Assert.Equal(900d, window.ClientSize.Width);
        Assert.True(window.Bounds.Width <= 900);
        window.FontSize = 20;
        window.RequestedThemeVariant = ThemeVariant.Dark;
        var vm = fixture.ViewModel;
        vm.Open(Path.Combine(shared!, "authoring-workspace"));
        vm.SelectProgram(PlanId);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Kind == SequenceRowKind.Metric));
        var originalRule = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
        vm.ImportRecording(recordingPath, "operator-feedback-" + original.RunId);
        fixture.NavigateTask(5);
        await fixture.WaitForReadinessAsync();
        Assert.Equal(original.RunId, vm.SelectedDataset!.Run.RunId);
        Assert.True(vm.HasRecordedReferences);
        var trigger = fixture.Control<Button>("Review recorded references");
        AuthoringUiFixture.Click(trigger);
        var dialog = Assert.Single(window.OwnedWindows);
        var review = fixture.Control<TextBox>("Full recorded reference review", dialog);
        Assert.Contains(original.RunId, review.Text);
        foreach (var use in original.ReferenceUses)
        {
            Assert.Contains(use.ReferenceId, review.Text);
            Assert.Contains(use.ReferenceSha256, review.Text);
            Assert.Contains(use.SourceRunId, review.Text);
        }
        Assert.True(review.IsReadOnly);
        Assert.True(review.IsFocused);
        Capture(dialog, shared!, "cross-stage3-recording-reference-review");
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.True(trigger.IsFocused);
        fixture.NavigateTask(0);
        Type(fixture, window, "Reference tolerance", "1");
        await fixture.WaitForReadinessAsync();
        Assert.Contains("Proposed preview: Pass", vm.RecordingReferenceReview);
        Capture(window, shared!, "cross-stage3-proposed-tolerance");
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        await fixture.WaitForReadinessAsync();
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.HasUncompiledSources);
        var changed = Assert.Single(vm.SelectedProgram!.Sidecar.ReferenceRules!);
        Assert.Equal(originalRule.RuleId, changed.RuleId);
        Assert.Equal(originalRule.ProducerStepId, changed.ProducerStepId);
        Assert.Equal(1, changed.Tolerance);
        await PrepareBenchFiles(fixture, Path.Combine(shared!, "updated-programs"));
        Capture(fixture.Window!.OwnedWindows.Single(), shared!, "cross-stage3-updated-receipt");
        Assert.Equal(originalBytes, File.ReadAllBytes(recordingPath));
        File.WriteAllText(Path.Combine(shared!, "edited-plan-id.txt"), PlanId);
    }

    private static async Task PrepareBenchFiles(AuthoringUiFixture fixture, string destination)
    {
        fixture.NavigateTask(4);
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare selected program for bench"));
        var dialog = Assert.IsType<BenchExportWindow>(Assert.Single(fixture.Window!.OwnedWindows));
        Type(fixture, dialog, "Bench files destination folder", destination);
        AuthoringUiFixture.Click(fixture.Control<Button>("Prepare bench copy", dialog));
        var status = fixture.Control<TextBlock>("Bench file preparation status", dialog);
        var watch = Stopwatch.StartNew();
        while (!status.Text!.StartsWith("Bench files prepared at", StringComparison.Ordinal))
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), status.Text);
            await Task.Delay(5);
            AuthoringUiFixture.Drain();
            if (fixture.Control<Button>("Prepare bench copy", dialog).IsEnabled && status.Text != "Checking saved files and preparing the bench copy…")
                Assert.StartsWith("Bench files prepared at", status.Text);
        }
        var receipt = fixture.Control<TextBox>("Prepared bench program receipt", dialog);
        Assert.Contains(PlanId, receipt.Text);
        Assert.Contains("SHA-256", receipt.Text);
    }

    private static void Type(AuthoringUiFixture fixture, Window window, string automationName, string value)
    {
        var box = fixture.Control<TextBox>(automationName, ReferenceEquals(window, fixture.Window) ? null : window);
        box.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(box.Focus()); box.SelectAll(); window.KeyTextInput(value); AuthoringUiFixture.Drain();
    }

    private static void Capture(Window window, string shared, string name)
    {
        if (Environment.GetEnvironmentVariable("HT_CAPTURE_UI") != "1") return;
        var root = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY") ?? Path.Combine(shared, "captures");
        Directory.CreateDirectory(root);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(root, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
