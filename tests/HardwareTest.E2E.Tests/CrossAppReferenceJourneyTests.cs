using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Features;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

/// Opt-in joined stages. Authoring stage 1 prepares the actual source artifacts;
/// authoring stage 3 later imports this exact stage 2 result and recompiles it.
[Collection("E2E")]
public sealed class CrossAppReferenceJourneyTests
{
    [AvaloniaFact]
    public async Task Stage2_Acquire_save_select_execute_export_and_preserve_actual_reference_evidence()
    {
        var root = JoinedRoot();
        var id = File.ReadAllText(Path.Combine(root, "plan-id.txt")).Trim();
        var prepared = Path.Combine(root, "prepared-programs", id);
        var installed = Path.Combine(AppContext.BaseDirectory, "Programs", id);
        if (Directory.Exists(installed))
        {
            var prior = BenchProgramHandoff.ValidateProgram(Path.Combine(installed, id + ".TapPlan"));
            var expected = BenchProgramHandoff.ValidateProgram(Path.Combine(prepared, id + ".TapPlan"));
            Assert.Equal(expected?.ExportId, prior?.ExportId);
        }
        else CopyDirectory(prepared, installed);
        Assert.NotNull(BenchProgramHandoff.ValidateProgram(Path.Combine(installed, id + ".TapPlan")));
        using var shown = E2EHarness.ShowMainWindow();
        var app = E2EHarness.RequireApp();
        app.SettingsStore.AppSettings.IsEngineerDebugMode = true;
        app.SettingsStore.AppSettings.ExportDirectory = Path.Combine(root, "exported-packages");
        app.SettingsStore.AppSettings.PreferRemovableExport = false;
        app.SettingsStore.AppSettings.RequireAttestationBeforeExport = false;
        await app.SettingsStore.SaveAppSettingsAsync();
        var window = shown.Window; window.Width = 1280; window.Height = 800;
        var main = shown.Main; main.IsNavPaneOpen = false;
        var run = main.RunTest;
        var store = app.Services.GetRequiredService<IRunStore>();
        Assert.Empty(await store.ListAsync());
        var references = app.Services.GetRequiredService<IBenchReferenceStore>();
        main.NavigateToPageId("RunTest");
        await run.ProgramSelection.RefreshProgramsAsync();
        run.ProgramSelection.SelectedProgram = Assert.Single(run.ProgramSelection.Programs, program => program.Id == id);
        try { await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady && run.References.Rules.Count == 1); }
        catch (TimeoutException error)
        {
            Capture(window, root, "joined-failed-program-load");
            throw new InvalidOperationException($"Joined program failed to become ready. Status: {run.Status}; availability: {run.ProgramAvailabilityMessage}; details: {run.ProgramDetailsText}; rules: {run.References.Rules.Count}.", error);
        }
        Assert.Equal(Path.Combine(installed, id + ".TapPlan"), run.ProgramSelection.SelectedProgram.Path);
        await run.RefreshBenchReferencesAsync();
        Assert.False(run.References.IsReady);
        Capture(window, root, "joined-operator-missing-reference");

        await Acquisition(true);
        await Confirm("GOLDEN-A");
        var acquisitionA = await Execute(false);
        Assert.Equal(RunExecutionScopes.ReferenceAcquisition, acquisitionA.ExecutionScope);
        Assert.Empty(acquisitionA.ReferenceUses);
        var profileA = await Promote(acquisitionA, "Golden A captured on this bench");

        // SelectionIncludesCleanup=false is explicitly authored for this mock-only
        // review. Two genuine selected acquisitions retain the mock DMM's mild
        // drift; their values are measured, never fabricated or edited.
        var producer = Assert.Single(run.ProgramSelection.SelectedProgram.ReferenceRules).ProducerStepId;
        run.StepTree.SelectedStep = Flatten(run.StepTree.Hierarchy).Single(step => Guid.TryParse(step.Id, out var stepId) && stepId == producer);
        await Confirm("GOLDEN-B");
        await Execute(true);
        run.StepTree.SelectedStep = Flatten(run.StepTree.Hierarchy).Single(step => Guid.TryParse(step.Id, out var stepId) && stepId == producer);
        var acquisitionB = await Execute(true);
        var profileB = await Promote(acquisitionB, "Golden B selected acquisition");
        Assert.True(Math.Abs(profileA.Values.Single().Value - profileB.Values.Single().Value) > 0.001,
            "The two genuinely measured golden units must distinguish the numeric acceptance oracle.");
        // A full acquisition performs authored cleanup and restores the same
        // deterministic mock input before both production executions.
        await Confirm("GOLDEN-RESET");
        await Execute(false);
        await Acquisition(false);
        await Choose(profileA.Id);
        await Confirm("PRODUCTION-UNIT");
        var pass = await Execute(false);
        Assert.Equal(RunResult.Passed, pass.ReferenceBackendResult);
        Assert.Equal(RunResult.Passed, pass.Result);
        Assert.True(Assert.Single(pass.ReferenceEvaluations).Passed);
        Assert.Equal(profileA.Id, Assert.Single(pass.ReferenceUses).ReferenceId);
        Capture(window, root, "joined-production-a-reference-passed");
        var passPath = Path.Combine(store.GetRunDirectory(pass.RunId), "run.json");
        var original = File.ReadAllBytes(passPath);
        await Choose(profileB.Id);
        await Confirm("PRODUCTION-UNIT");
        var fail = await Execute(false);
        Assert.Equal(RunResult.Passed, fail.ReferenceBackendResult);
        Assert.Equal(RunResult.Failed, fail.Result);
        Assert.False(Assert.Single(fail.ReferenceEvaluations).Passed);
        Assert.Equal(profileB.Id, Assert.Single(fail.ReferenceUses).ReferenceId);
        Assert.True(run.StepTree.SuiteFailedCount > 0);
        Assert.Equal("Fail", Assert.Single(run.StepTree.Stages, stage => stage.Step is null).ChipText);
        Assert.Contains(run.StepTree.StepRows, row => row.IsReadOnlyEvidence && row.ChipText == "Fail");
        Capture(window, root, "joined-production-b-reference-failed");
        var previousTheme = app.RequestedThemeVariant;
        foreach (var (dark, width, height, font) in new[] { (false, 1280, 800, 14), (true, 1280, 800, 14), (false, 900, 600, 20), (true, 900, 600, 20) })
        {
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Width = width; window.Height = height; window.FontSize = font;
            Settle(window);
            Assert.Equal((double)width, window.Bounds.Width);
            var failedItem = Assert.Single(run.StepTree.StepListItems, item => item.Step is
            { IsReadOnlyEvidence: true, Children.Count: 0, ChipText: "Fail" });
            var container = Assert.Single(window.GetVisualDescendants().OfType<ListBoxItem>(), item => ReferenceEquals(item.DataContext, failedItem));
            container.BringIntoView(); Settle(window);
            Capture(window, root, $"joined-reference-failure-{(dark ? "dark" : "light")}-{width}x{height}-font{font}");
            OperatorWorkflowUiTests.Inside(container, window);
            OperatorWorkflowUiTests.Inside(Named<Button>(window, "View saved result"), window);
        }
        app.RequestedThemeVariant = previousTheme;
        window.Width = 1280; window.Height = 800; window.FontSize = 14; Settle(window);

        await Choose(profileA.Id);
        await Confirm("PRODUCTION-UNIT");
        var repeated = await Execute(false);
        Assert.Equal(RunResult.Passed, repeated.Result);
        Assert.True(Assert.Single(repeated.ReferenceEvaluations).Passed);
        Assert.Equal(profileA.Id, Assert.Single(repeated.ReferenceUses).ReferenceId);
        Assert.Equal(0, run.StepTree.SuiteFailedCount);
        Assert.Equal("Pass", Assert.Single(run.StepTree.Stages, stage => stage.Step is null).ChipText);
        Assert.DoesNotContain(run.StepTree.StepRows, row => row.IsReadOnlyEvidence && row.ChipText == "Fail");
        Capture(window, root, "joined-production-a-repeat-clears-reference-failure");

        await Open(pass.RunId);
        Assert.Contains(profileA.Id, main.Results.RunIdentityText, StringComparison.Ordinal);
        var report = Assert.Single(pass.Reports, artifact => artifact.Kind == ReportKinds.Status && artifact.Role == ReportArtifactRoles.Working);
        var pdfBytes = File.ReadAllBytes(report.PdfPath);
        Assert.Contains(profileA.Id, PdfText(pdfBytes), StringComparison.Ordinal);
        Assert.Contains(profileA.SourceRunId, PdfText(pdfBytes), StringComparison.Ordinal);
        await main.Results.RefreshCommand.ExecuteAsync();
        await main.Results.OpenRunByIdAsync(pass.RunId);
        main.Results.SelectedExportTarget = Assert.Single(main.Results.ExportTargets,
            target => Path.GetFullPath(target.RootPath) == Path.GetFullPath(app.SettingsStore.AppSettings.ExportDirectory));
        await main.Results.ExportPackageCommand.ExecuteAsync();
        Assert.Contains("Exported package", main.Results.Status, StringComparison.Ordinal);
        var exported = Assert.Single(Directory.EnumerateFiles(app.SettingsStore.AppSettings.ExportDirectory, "run.json", SearchOption.AllDirectories));
        var exportedRecord = JsonSerializer.Deserialize(File.ReadAllText(exported), AppJsonContext.Default.TestRunRecord)!;
        Assert.Equal(pass.RunId, exportedRecord.RunId);
        Assert.Equal(profileA.Id, Assert.Single(exportedRecord.ReferenceUses).ReferenceId);
        var feedback = Path.Combine(root, "operator-result"); Directory.CreateDirectory(feedback);
        File.Copy(exported, Path.Combine(feedback, "run.json"));
        File.WriteAllText(Path.Combine(root, "pass-run-id.txt"), pass.RunId);
        File.WriteAllText(Path.Combine(root, "profile-a-id.txt"), profileA.Id);
        File.WriteAllText(Path.Combine(root, "profile-b-id.txt"), profileB.Id);
        Capture(window, root, "joined-operator-frozen-result");
        CopyDirectoryTree(Path.Combine(root, "bench-data"), Path.Combine(root, "native-compatible-bench"));
        await references.RetireAsync(profileB.Id, "Replaced after the review acquisition.");
        await Choose(profileA.Id);
        Assert.Equal(original, File.ReadAllBytes(passPath));
        Assert.Equal(pdfBytes, File.ReadAllBytes(report.PdfPath));
        var reloaded = await store.LoadAsync(pass.RunId);
        Assert.Equal(profileA.ContentSha256, Assert.Single(reloaded!.ReferenceUses).ReferenceSha256);

        async Task Acquisition(bool enabled)
        {
            main.NavigateToPageId("RunTest");
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Golden references"));
            await E2EHarness.WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Golden references"));
            var dialog = window.OwnedWindows.Single(child => child.Title == "Golden references");
            var mode = Named<CheckBox>(dialog, "Reference acquisition mode");
            mode.BringIntoView(); Settle(dialog); Assert.True(mode.Focus());
            if (mode.IsChecked != enabled)
            {
                dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            }
            Settle(dialog); Assert.Equal(enabled, run.References.IsAcquisition);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(window);
        }

        async Task Confirm(string dut)
        {
            main.NavigateToPageId("RunTest");
            Settle(window);
            var priorDut = run.SessionPanel.Session.DutSerial;
            var priorSession = run.SessionPanel.Session.SessionId;
            if (!string.IsNullOrWhiteSpace(priorDut) && priorDut != dut)
            {
                OperatorWorkflowUiTests.Click(Named<Button>(window, "Change session"));
                Settle(window);
                Assert.Empty(run.SessionPanel.Session.DutSerial);
            }
            run.SessionPanel.ShowSessionForm = true;
            run.SessionPanel.DutSerialInput = dut;
            run.SessionPanel.OperatorInput = "Joined workflow technician";
            run.SessionPanel.DutPartInput = "REVIEW-PART";
            run.SessionPanel.DutRevisionInput = "A";
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.False(run.SessionPanel.SessionBlocked);
            Assert.Equal(dut, run.SessionPanel.Session.DutSerial);
            if (!string.IsNullOrWhiteSpace(priorDut))
            {
                if (priorDut == dut) Assert.Equal(priorSession, run.SessionPanel.Session.SessionId);
                else Assert.NotEqual(priorSession, run.SessionPanel.Session.SessionId);
            }
            Settle(window);
        }

        async Task<TestRunRecord> Execute(bool selected)
        {
            main.NavigateToPageId("RunTest");
            var command = selected ? run.Run.RunSelectedCommand : run.Run.RunCommand;
            var task = command.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator) run.ContinueOperatorAttention();
                return task.IsCompleted && !run.IsRunning;
            }, TimeSpan.FromMinutes(2));
            await task;
            var savedRunId = Assert.IsType<string>(run.LastRunId);
            var saved = await store.LoadAsync(savedRunId);
            Assert.NotNull(saved); Assert.Equal(id, saved.PlanId);
            Assert.NotNull(saved.ReferenceContext);
            return saved;
        }

        async Task Open(string runId)
        {
            main.NavigateToPageId("Results");
            await main.Results.RefreshCommand.ExecuteAsync();
            await main.Results.OpenRunByIdAsync(runId);
            Assert.Equal(runId, main.Results.OpenedRun?.RunId); Settle(window);
        }

        async Task<BenchReferenceProfile> Promote(TestRunRecord source, string name)
        {
            await Open(source.RunId);
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Save golden reference from this run"));
            await E2EHarness.WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Save golden reference"));
            var dialog = window.OwnedWindows.Single(child => child.Title == "Save golden reference");
            var field = Named<TextBox>(dialog, "Golden reference name");
            field.BringIntoView(); Settle(dialog); Assert.True(field.Focus()); field.SelectAll(); dialog.KeyTextInput(name);
            OperatorWorkflowUiTests.Click(Named<Button>(dialog, "Save new golden reference"));
            await E2EHarness.WaitUntilAsync(() => !main.Results.ReferencePromotion.IsSaving
                && main.Results.ReferencePromotion.Status.StartsWith("Saved ", StringComparison.Ordinal));
            var profile = Assert.Single(await references.ListAsync(), entry => entry.Profile?.Name == name).Profile!;
            Assert.Equal(source.RunId, profile.SourceRunId);
            Capture(dialog, root, name.Contains("Golden A", StringComparison.Ordinal) ? "joined-golden-a-saved" : "joined-golden-b-saved");
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(window);
            return profile;
        }

        async Task Choose(string profileId)
        {
            main.NavigateToPageId("RunTest");
            await run.RefreshBenchReferencesAsync();
            OperatorWorkflowUiTests.Click(Named<Button>(window, "Golden references"));
            await E2EHarness.WaitUntilAsync(() => window.OwnedWindows.Any(child => child.Title == "Golden references"));
            var dialog = window.OwnedWindows.Single(child => child.Title == "Golden references");
            var row = Assert.Single(run.References.Rules);
            var choice = Assert.Single(row.Choices, candidate => candidate.Profile.Id == profileId);
            var combo = Named<ComboBox>(dialog, row.AutomationName);
            combo.SelectedItem = choice; Settle(dialog);
            Assert.Equal(profileId, row.SelectedChoice?.Profile.Id);
            OperatorWorkflowUiTests.Click(Named<Button>(dialog, "Save reference choices"));
            await E2EHarness.WaitUntilAsync(() => app.SettingsStore.AppSettings.BenchReferenceSelections.Any(selection => selection.ReferenceId == profileId));
            Capture(dialog, root, "joined-golden-input-selected-" + profileId);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(window);
        }
    }

    [AvaloniaFact]
    public async Task Stage4_Restart_reject_changed_revision_and_preserve_previous_result_and_retired_references()
    {
        var root = JoinedRoot();
        var id = File.ReadAllText(Path.Combine(root, "edited-plan-id.txt")).Trim();
        using var shown = E2EHarness.ShowMainWindow();
        var main = shown.Main; var run = main.RunTest;
        var store = E2EHarness.RequireApp().Services.GetRequiredService<IRunStore>();
        var references = E2EHarness.RequireApp().Services.GetRequiredService<IBenchReferenceStore>();
        var passId = File.ReadAllText(Path.Combine(root, "pass-run-id.txt"));
        var referenceId = File.ReadAllText(Path.Combine(root, "profile-a-id.txt"));
        var retiredId = File.ReadAllText(Path.Combine(root, "profile-b-id.txt"));
        var passPath = Path.Combine(store.GetRunDirectory(passId), "run.json");
        var original = File.ReadAllBytes(passPath);
        Assert.Equal(referenceId, (await references.LoadAsync(referenceId)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => references.LoadAsync(retiredId));
        var updated = Path.Combine(root, "updated-programs", id);
        var installed = Path.Combine(AppContext.BaseDirectory, "Programs", id);
        CopyDirectory(updated, installed, overwrite: true);
        Assert.NotNull(BenchProgramHandoff.ValidateProgram(Path.Combine(installed, id + ".TapPlan")));
        main.NavigateToPageId("RunTest");
        await run.ProgramSelection.RefreshProgramsAsync();
        run.ProgramSelection.SelectedProgram = Assert.Single(run.ProgramSelection.Programs, program => program.Id == id);
        await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady);
        await run.RefreshBenchReferencesAsync();
        Assert.False(run.References.IsReady);
        Assert.Contains("different program revision", Assert.Single(run.References.Rules).MissingReason, StringComparison.Ordinal);
        var lastId = run.LastRunId;
        run.SessionPanel.DutSerialInput = "AFTER-REDEPLOY"; run.SessionPanel.OperatorInput = "Joined workflow technician";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        await run.Run.RunCommand.ExecuteAsync();
        Assert.False(run.IsRunning); Assert.Equal(lastId, run.LastRunId);
        main.NavigateToPageId("Results"); await main.Results.OpenRunByIdAsync(passId);
        Assert.Equal(referenceId, Assert.Single(main.Results.OpenedRun!.ReferenceUses).ReferenceId);
        Assert.Equal(original, File.ReadAllBytes(passPath));
        Capture(shown.Window, root, "joined-old-result-after-redeploy");

        // The changed artifact must be qualified with a genuinely new capture;
        // demonstrating only the refusal above would leave the recovery path untested.
        main.NavigateToPageId("RunTest");
        OperatorWorkflowUiTests.Click(Named<Button>(shown.Window, "Golden references"));
        await E2EHarness.WaitUntilAsync(() => shown.Window.OwnedWindows.Any(child => child.Title == "Golden references"));
        var choices = shown.Window.OwnedWindows.Single(child => child.Title == "Golden references");
        var acquisition = Named<CheckBox>(choices, "Reference acquisition mode");
        acquisition.BringIntoView(); Settle(choices); Assert.True(acquisition.Focus());
        choices.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        choices.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Assert.True(run.References.IsAcquisition);
        choices.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await ConfirmAndExecute("REQUALIFY-CHANGED-REVISION");
        var acquired = (await store.LoadAsync(Assert.IsType<string>(run.LastRunId)))!;
        Assert.Equal(RunExecutionScopes.ReferenceAcquisition, acquired.ExecutionScope);
        Assert.NotEqual((await store.LoadAsync(passId))!.ReferenceContext!.SidecarSha256, acquired.ReferenceContext!.SidecarSha256);
        main.NavigateToPageId("Results");
        await main.Results.OpenRunByIdAsync(acquired.RunId);
        OperatorWorkflowUiTests.Click(Named<Button>(shown.Window, "Save golden reference from this run"));
        await E2EHarness.WaitUntilAsync(() => shown.Window.OwnedWindows.Any(child => child.Title == "Save golden reference"));
        var promotion = shown.Window.OwnedWindows.Single(child => child.Title == "Save golden reference");
        var name = Named<TextBox>(promotion, "Golden reference name");
        name.BringIntoView(); Settle(promotion); Assert.True(name.Focus()); name.SelectAll();
        promotion.KeyTextInput("Golden A qualified for revised program");
        OperatorWorkflowUiTests.Click(Named<Button>(promotion, "Save new golden reference"));
        await E2EHarness.WaitUntilAsync(() => main.Results.ReferencePromotion.Status.StartsWith("Saved ", StringComparison.Ordinal));
        var replacement = Assert.Single(await references.ListAsync(), entry => entry.Profile?.SourceRunId == acquired.RunId).Profile!;
        Assert.NotEqual(referenceId, replacement.Id);
        promotion.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        main.NavigateToPageId("RunTest");
        await run.RefreshBenchReferencesAsync();
        OperatorWorkflowUiTests.Click(Named<Button>(shown.Window, "Golden references"));
        await E2EHarness.WaitUntilAsync(() => shown.Window.OwnedWindows.Any(child => child.Title == "Golden references"));
        choices = shown.Window.OwnedWindows.Single(child => child.Title == "Golden references");
        acquisition = Named<CheckBox>(choices, "Reference acquisition mode");
        acquisition.BringIntoView(); Settle(choices); Assert.True(acquisition.Focus());
        choices.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        choices.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Assert.False(run.References.IsAcquisition);
        var row = Assert.Single(run.References.Rules);
        Named<ComboBox>(choices, row.AutomationName).SelectedItem = Assert.Single(row.Choices, choice => choice.Profile.Id == replacement.Id);
        Settle(choices);
        OperatorWorkflowUiTests.Click(Named<Button>(choices, "Save reference choices"));
        await E2EHarness.WaitUntilAsync(() => E2EHarness.RequireApp().SettingsStore.AppSettings.BenchReferenceSelections.Any(selection => selection.ReferenceId == replacement.Id));
        choices.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await ConfirmAndExecute("PRODUCTION-REVISED-PROGRAM");
        var revised = (await store.LoadAsync(Assert.IsType<string>(run.LastRunId)))!;
        Assert.Equal(RunResult.Passed, revised.Result);
        Assert.True(Assert.Single(revised.ReferenceEvaluations).Passed);
        Assert.Equal(replacement.Id, Assert.Single(revised.ReferenceUses).ReferenceId);
        Assert.Equal(original, File.ReadAllBytes(passPath));
        Capture(shown.Window, root, "joined-revised-program-requalified-and-passed");

        async Task ConfirmAndExecute(string dut)
        {
            run.SessionPanel.ShowSessionForm = true;
            run.SessionPanel.DutSerialInput = dut;
            run.SessionPanel.OperatorInput = "Joined workflow technician";
            await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
            Assert.False(run.SessionPanel.SessionBlocked);
            var execution = run.Run.RunCommand.ExecuteAsync();
            await E2EHarness.WaitUntilAsync(() =>
            {
                if (run.Interaction.IsAwaitingOperator) run.ContinueOperatorAttention();
                return execution.IsCompleted && !run.IsRunning;
            }, TimeSpan.FromMinutes(2));
            await execution;
        }
    }

    private static string JoinedRoot()
    {
        var root = Environment.GetEnvironmentVariable("HT_CROSS_APP_REVIEW_DIRECTORY");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(root), "Set HT_CROSS_APP_REVIEW_DIRECTORY and run joined authoring/operator stages in order.");
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.Equal("1", Environment.GetEnvironmentVariable("HT_CROSS_APP_OPERATOR_PERSISTENCE"));
        return root!;
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);
    private static IEnumerable<HierarchyStepViewModel> Flatten(IEnumerable<HierarchyStepViewModel> nodes)
    {
        foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; }
    }
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string root, string name)
    {
        var directory = Path.Combine(root, "captures"); Directory.CreateDirectory(directory);
        Settle(window); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    private static void CopyDirectory(string source, string destination, bool overwrite = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);
    }
    private static void CopyDirectoryTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
    private static string PdfText(byte[] bytes)
    {
        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(bytes)));
        return string.Join("\n", Enumerable.Range(1, pdf.GetNumberOfPages()).Select(page => PdfTextExtractor.GetTextFromPage(pdf.GetPage(page))));
    }
}
