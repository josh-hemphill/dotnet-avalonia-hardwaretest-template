using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Features.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorSigningWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 800, 14)]
    [InlineData(true, 1280, 800, 14)]
    [InlineData(false, 900, 600, 20)]
    [InlineData(true, 900, 600, 20)]
    public async Task Quality_reviewer_can_cancel_correct_PIN_and_deliver_exact_evidence_with_keyboard_focus(
        bool dark, int width, int height, int font)
    {
        var app = E2EHarness.RequireApp();
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = width; window.Height = height; window.FontSize = font;
        var originalTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var main = shown.Main;
        main.IsNavPaneOpen = width >= 1280;
        main.NavigateToPageId("Results");
        var originalPage = main.CurrentPage;
        var root = Path.Combine(app.SettingsStore.RootDirectory, "signing-persona-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
            ExportDirectory = Path.Combine(root, "chosen-delivery"),
        };
        var store = app.Services.GetRequiredService<IRunStore>();
        var reports = app.Services.GetRequiredService<IReportService>();
        var run = new TestRunRecord
        {
            RunId = "signing-persona-" + Guid.NewGuid().ToString("N"),
            PlanId = "quality-persona",
            PlanName = "Quality evidence review",
            ExecutionScope = RunExecutionScopes.FullSuite,
            DutSerial = "QUALITY-DUT-42",
            OperatorName = "Original test operator",
            SessionId = Guid.NewGuid().ToString("N"),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(run);
        await reports.GenerateReportsAsync(run, [ReportKinds.Certification]);
        var originalWorking = await File.ReadAllBytesAsync(Assert.Single(run.Reports).PdfPath);
        var broker = new PinBroker();
        var attestation = new ReportAttestationService(broker, store, settings,
            reports: new Lazy<IReportService>(() => reports));
        var export = new ExportTargetService(settings, root, removableRoots: () =>
        [
            new ExportTarget { Id = "first", DisplayName = "Other delivery", RootPath = Path.Combine(root, "other-delivery"), IsRemovable = true },
        ]);
        var results = new ResultsViewModel(store, reports, exportTargets: export, attestation: attestation, settings: settings);
        try
        {
            // Real production shell/view/service; only the credential reader is a deterministic mock.
            main.CurrentPage = results;
            await results.RefreshCommand.ExecuteAsync();
            results.SelectedRun = Assert.Single(results.Runs, item => item.RunId == run.RunId);
            await results.OpenCommand.ExecuteAsync();
            results.SelectedExportTarget = results.ExportTargets.Single(target => target.Id == "configured");
            await E2EHarness.WaitUntilAsync(() => window.GetVisualDescendants().OfType<ResultsView>()
                .Any(item => item.IsEffectivelyVisible && ReferenceEquals(item.DataContext, results)));
            using (var layout = window.CaptureRenderedFrame()) Assert.NotNull(layout);
            Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(window.GetVisualDescendants().OfType<ResultsView>(), item =>
                item.IsEffectivelyVisible && ReferenceEquals(item.DataContext, results));
            var exportButton = ByName<Button>(view, "Export package");
            exportButton.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.True(exportButton.Focus());
            OperatorWorkflowUiTests.Click(exportButton);
            await E2EHarness.PumpUiAsync();
            Assert.True(results.ShowAttestationPrompt);
            var sign = ByName<Button>(view, "Tap or insert badge");
            Assert.True(sign.IsFocused);
            Assert.False(view.FindControl<Grid>("ResultsLayout")!.IsEffectivelyEnabled);
            foreach (var name in new[] { "Tap or insert badge", "Record presence without signing", "Cancel report signing" })
                OperatorWorkflowUiTests.Inside(ByName<Button>(view, name), window);
            Tab(window);
            Assert.True(ByName<Button>(view, "Record presence without signing").IsFocused);
            Tab(window);
            Assert.True(ByName<Button>(view, "Cancel report signing").IsFocused);
            Tab(window);
            Assert.True(sign.IsFocused);
            Capture(window, "quality-signing-prompt", dark, width, height, font);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await E2EHarness.PumpUiAsync();
            Assert.False(results.ShowAttestationPrompt);
            Assert.True(exportButton.IsFocused);
            Assert.False(Directory.Exists(settings.ExportDirectory));
            Assert.Empty((await store.LoadAsync(run.RunId))!.Attestations);

            OperatorWorkflowUiTests.Click(exportButton);
            OperatorWorkflowUiTests.Click(ByName<Button>(view, "Tap or insert badge"));
            await E2EHarness.WaitUntilAsync(() => results.ShowAttestationPin && !results.IsCapturingAttestation);
            var pin = ByName<TextBox>(view, "Badge PIN");
            Assert.True(pin.IsFocused);
            Assert.NotNull(AutomationProperties.GetLabeledBy(pin));
            OperatorWorkflowUiTests.Inside(pin, window);
            Capture(window, "quality-signing-pin", dark, width, height, font);
            window.KeyTextInput("incorrect");
            OperatorWorkflowUiTests.Click(ByName<Button>(view, "Sign with badge PIN"));
            await E2EHarness.WaitUntilAsync(() => !results.IsCapturingAttestation);
            Assert.True(results.ShowAttestationPrompt);
            Assert.Contains("Incorrect PIN", results.AttestationPromptStatus, StringComparison.Ordinal);
            Assert.Empty(pin.Text ?? string.Empty);
            Assert.False(Directory.Exists(settings.ExportDirectory));
            Capture(window, "quality-signing-retry", dark, width, height, font);
            Assert.True(pin.Focus());
            window.KeyTextInput(PinBroker.Pin);
            OperatorWorkflowUiTests.Click(ByName<Button>(view, "Sign with badge PIN"));
            await E2EHarness.WaitUntilAsync(() => !results.IsCapturingAttestation && !results.ShowAttestationPrompt);
            Assert.Contains(settings.ExportDirectory, results.Status, StringComparison.Ordinal);
            var package = Path.Combine(settings.ExportDirectory, "run-" + run.RunId);
            Assert.True(File.Exists(Path.Combine(package, "run.json")));
            Assert.True(File.Exists(Path.Combine(package, "certification.pdf")));
            Assert.True(File.Exists(Path.Combine(package, "certification.attestation.json")));
            Assert.False(Directory.Exists(Path.Combine(root, "other-delivery")));
            Assert.Equal(run.RunId, results.OpenedRun?.RunId);
            var saved = (await store.LoadAsync(run.RunId))!;
            Assert.Equal(originalWorking, await File.ReadAllBytesAsync(saved.Reports.Single(report => ReportArtifactRoles.IsWorking(report.Role)).PdfPath));
            Assert.Contains(saved.Reports, report => ReportArtifactRoles.IsIssued(report.Role));
            Capture(window, "quality-export-completed", dark, width, height, font);
        }
        finally
        {
            results.CancelPendingReportAction();
            main.CurrentPage = originalPage;
            app.RequestedThemeVariant = originalTheme;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static T ByName<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);

    private static void Tab(Window window)
    {
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string state, bool dark, int width, int height, int font)
    {
        using (var warm = window.CaptureRenderedFrame()) Assert.NotNull(warm);
        Dispatcher.UIThread.RunJobs();
        if (!OperatorGraphicalReviewCapture.CaptureEnabled) return;
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")!;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        image.Save(Path.Combine(directory, $"{state}-{(dark ? "dark" : "light")}-{width}x{height}-font{font}.png"),
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class PinBroker : IOperatorCredentialBroker
    {
        internal const string Pin = "246810";
        private readonly MockOperatorCredentialBroker _mock = new();
        public bool IsMock => true;
        public bool CanSign => true;
        public string? SigningAlgorithm => _mock.SigningAlgorithm;
        public string StatusText => "Mock PIN reader ready";
        public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => _mock.WaitForPresenceAsync(timeout, cancellationToken);
        public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential,
            string? pin = null, CancellationToken cancellationToken = default)
            => string.IsNullOrEmpty(pin)
                ? Task.FromResult(CredentialSignResult.NeedPin("Enter badge PIN to sign."))
                : pin == Pin
                    ? _mock.TrySignPayloadAsync(payload, credential, pin, cancellationToken)
                    : Task.FromResult(CredentialSignResult.Failed("Incorrect PIN (2 retries left).", 2));
    }
}
