using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Reporting;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ReportActionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "report-actions-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Standalone_sign_creates_revision_and_previews_without_print_or_save()
    {
        var (run, vm, actions, _) = await SetupAsync();
        await vm.SignCommand.ExecuteAsync();
        Assert.True(vm.ShowSigningPrompt);
        Assert.Equal("Sign report", vm.SignButtonLabel);
        await vm.SignAndContinueCommand.ExecuteAsync();
        var loaded = (await new FileRunStore(_root).LoadAsync(run.RunId))!;
        var issued = ReportRevisions.Latest(loaded, ReportKinds.Certification)!;
        Assert.Equal(issued.PdfPath, vm.PdfPath);
        Assert.Contains("Digitally signed", vm.ReportSummary);
        Assert.Empty(actions.Prints);
        Assert.Empty(actions.Saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Policy_gated_save_or_print_resumes_exactly_once(bool save)
    {
        var (run, vm, actions, _) = await SetupAsync();
        if (save) await vm.SaveCopyCommand.ExecuteAsync();
        else await vm.PrintCommand.ExecuteAsync();
        Assert.True(vm.ShowSigningPrompt);
        Assert.Equal("Sign and continue", vm.SignButtonLabel);
        Assert.Empty(actions.Prints);
        Assert.Empty(actions.Saves);
        await vm.SignAndContinueCommand.ExecuteAsync();
        await vm.SignAndContinueCommand.ExecuteAsync();
        var issued = ReportRevisions.Latest((await new FileRunStore(_root).LoadAsync(run.RunId))!, ReportKinds.Certification)!;
        Assert.Equal(issued.PdfPath, Assert.Single(save ? actions.Saves : actions.Prints));
        if (save) Assert.Equal(await File.ReadAllBytesAsync(issued.PdfPath), await File.ReadAllBytesAsync(actions.Destination));
    }

    [Fact]
    public async Task Selecting_older_revision_keeps_exact_bytes_for_save_print_and_viewer()
    {
        var (run, vm, actions, service) = await SetupAsync();
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var first = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        await File.WriteAllTextAsync(ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification)!, "changed working");
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        await vm.LoadFromPathAsync(first.PdfPath);
        await vm.SaveCopyCommand.ExecuteAsync();
        await vm.PrintCommand.ExecuteAsync();
        await vm.OpenInViewerCommand.ExecuteAsync();
        Assert.Equal(first.PdfPath, Assert.Single(actions.Saves));
        Assert.Equal(first.PdfPath, Assert.Single(actions.Prints));
        Assert.Equal(first.PdfPath, Assert.Single(actions.Opens));
        Assert.Contains("revision 1", vm.ReportSummary);
        Assert.Equal(await File.ReadAllBytesAsync(first.PdfPath), await File.ReadAllBytesAsync(actions.Destination));
    }

    [Fact]
    public async Task Failed_historical_verification_cannot_silently_issue_or_print_another_revision()
    {
        var (run, vm, actions, service) = await SetupAsync();
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var issued = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        await File.WriteAllTextAsync(issued.PdfPath, "tampered");
        await vm.LoadFromPathAsync(issued.PdfPath);
        await vm.PrintCommand.ExecuteAsync();
        Assert.False(vm.ShowSigningPrompt);
        Assert.Empty(actions.Prints);
        Assert.Contains("Verification failed", vm.ReportSummary);
        Assert.Single((await new FileRunStore(_root).LoadAsync(run.RunId))!.Attestations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancel_or_selection_navigation_suppresses_late_signing_resume(bool cancel)
    {
        var store = new FileRunStore(_root);
        var original = await SeedAsync(store, "original");
        var other = await SeedAsync(store, "other");
        var delayed = new DelayedAttestation();
        var actions = new Actions(Path.Combine(_root, "saved.pdf"));
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), attestation: delayed, printer: actions, desktop: actions);
        vm.UiScheduler = action => action();
        await vm.LoadFromPathAsync(original.Reports[0].PdfPath);
        await vm.PrintCommand.ExecuteAsync();
        var signing = vm.SignAndContinueCommand.ExecuteAsync();
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) vm.CancelPendingAction();
        else await vm.LoadFromPathAsync(other.Reports[0].PdfPath);
        delayed.Release.TrySetResult();
        await signing;
        Assert.Equal(original.RunId, delayed.CapturedRunId);
        Assert.Empty(actions.Prints);
        Assert.False(vm.ShowSigningPrompt);
        if (!cancel) Assert.Equal(other.Reports[0].PdfPath, vm.PdfPath);
    }

    [Fact]
    public async Task Readonly_working_run_cannot_sign_or_satisfy_required_policy()
    {
        var (run, vm, actions, _) = await SetupAsync();
        run.SchemaVersion = SchemaVersions.TestRunRecord + 1;
        await File.WriteAllTextAsync(Path.Combine(new FileRunStore(_root).GetRunDirectory(run.RunId), "run.json"),
            JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        await vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        await vm.SignCommand.ExecuteAsync();
        Assert.False(vm.ShowSigningPrompt);
        Assert.Contains("newer schema", vm.Status);
        await vm.PrintCommand.ExecuteAsync();
        Assert.Empty(actions.Prints);
        Assert.False(vm.ShowSigningPrompt);
    }

    [Fact]
    public async Task Atomic_save_overwrites_longer_destination_with_exact_bytes_and_preserves_source()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.pdf");
        var destination = Path.Combine(_root, "copy.pdf");
        byte[] bytes = [0, 255, 5, 10, 13, 128];
        await File.WriteAllBytesAsync(source, bytes);
        await File.WriteAllBytesAsync(destination, new byte[4096]);
        await ReportDesktopActions.CopyToLocalPathAsync(source, destination);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
        await Assert.ThrowsAsync<IOException>(() => ReportDesktopActions.CopyToLocalPathAsync(source, source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Results_pending_print_cannot_resume_after_cancel_or_opened_run_changes(bool cancel)
    {
        var store = new FileRunStore(_root);
        var first = await SeedAsync(store, "results-first");
        var other = await SeedAsync(store, "results-other");
        var delayed = new DelayedAttestation();
        var vm = new ResultsViewModel(store, new FakeReportService(), attestation: delayed,
            settings: new AppSettings { RequireAttestationBeforeExport = true });
        vm.UiScheduler = action => action();
        vm.OpenedRun = first;
        var prints = new List<string>();
        vm.CertifiedPrintReady += (_, path) => prints.Add(path);
        await vm.RequestCertifiedPrintAsync(first.Reports[0].PdfPath);
        Assert.True(vm.ShowAttestationPrompt);
        var capture = vm.CaptureAttestationCommand.ExecuteAsync();
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) vm.CancelPendingReportAction();
        else vm.OpenedRun = other;
        delayed.Release.TrySetResult();
        await capture;
        Assert.Equal(first.RunId, delayed.CapturedRunId);
        Assert.Empty(prints);
        Assert.False(vm.ShowAttestationPrompt);
        if (!cancel) Assert.Same(other, vm.OpenedRun);
    }

    [Fact]
    public async Task Presence_attestation_label_and_cancelled_save_do_not_trigger_printing()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store, "presence");
        var settings = new AppSettings { RequireAttestationBeforeExport = true, AllowPresenceInLieuOfSigning = true };
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: false), store, settings);
        var actions = new Actions(Path.Combine(_root, "unused.pdf")) { CancelSave = true };
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), attestation: service, printer: actions, desktop: actions, settings: settings);
        vm.UiScheduler = action => action();
        await vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        Assert.Contains("Unsigned", vm.ReportSummary);
        await vm.SaveCopyCommand.ExecuteAsync();
        await vm.UsePresenceCommand.ExecuteAsync();
        Assert.Contains("Presence attested", vm.ReportSummary);
        Assert.Equal("Save cancelled.", vm.Status);
        Assert.Empty(actions.Saves);
        Assert.Empty(actions.Prints);
        Assert.False(File.Exists(actions.Destination));
    }

    [Fact]
    public async Task Actions_are_not_ready_during_new_selection_lookup()
    {
        var store = new FileRunStore(_root);
        var first = await SeedAsync(store, "first");
        var second = await SeedAsync(store, "second");
        var delayed = new DelayedStore(store, second.RunId);
        var actions = new Actions(Path.Combine(_root, "saved.pdf"));
        var vm = new ReportPreviewViewModel(delayed, new FakeReportService(), printer: actions, desktop: actions)
        { UiScheduler = action => action(), PreviewRenderer = _ => [] };
        await vm.LoadFromPathAsync(first.Reports[0].PdfPath);
        var loading = vm.LoadFromPathAsync(second.Reports[0].PdfPath);
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.PrintCommand.ExecuteAsync();
        await vm.SaveCopyCommand.ExecuteAsync();
        await vm.SignCommand.ExecuteAsync();
        Assert.Empty(actions.Prints);
        Assert.Empty(actions.Saves);
        Assert.False(vm.ShowSigningPrompt);
        delayed.Release.TrySetResult();
        await loading;
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(second.Reports[0].PdfPath, Assert.Single(actions.Prints));
    }

    [Fact]
    public async Task Cancelling_selection_during_render_clears_busy_and_ignores_late_errors()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store, "render");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ReportPreviewViewModel(store, new FakeReportService())
        {
            UiScheduler = action => action(),
            PreviewRenderer = _ =>
            {
                started.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                throw new IOException("stale rendering error");
            },
        };
        var loading = vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsBusy);
        vm.CancelPendingAction();
        Assert.False(vm.IsBusy);
        var status = vm.Status;
        release.TrySetResult();
        await loading;
        Assert.False(vm.IsBusy);
        Assert.Equal(status, vm.Status);
    }

    [Fact]
    public async Task Cancelled_signing_ignores_already_queued_pin_callback()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store, "queued-pin");
        var delayed = new DelayedAttestation
        {
            Result = new ReportAttestationResult { PinRequired = true, Message = "old PIN request", Credential = new OperatorCredential() },
        };
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), attestation: delayed)
        { UiScheduler = action => action(), PreviewRenderer = _ => [] };
        await vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        await vm.SignCommand.ExecuteAsync();
        var signing = vm.SignAndContinueCommand.ExecuteAsync();
        await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.UiScheduler = action => { queue.Enqueue(action); queued.TrySetResult(); };
        delayed.Release.TrySetResult();
        await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CancelPendingAction();
        vm.UiScheduler = action => action();
        while (queue.TryDequeue(out var callback)) callback();
        await signing;
        Assert.False(vm.ShowSigningPin);
        Assert.False(vm.ShowSigningPrompt);
        Assert.DoesNotContain("old PIN request", vm.Status);
    }

    [Fact]
    public async Task Lookup_failure_keeps_preview_usable_with_verification_failure()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store, "lookup-error");
        var broken = new DelayedStore(store, run.RunId) { FailLookup = true };
        var vm = new ReportPreviewViewModel(broken, new FakeReportService())
        { UiScheduler = action => action(), PreviewRenderer = _ => [] };
        await vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        Assert.Equal(run.Reports[0].PdfPath, vm.PdfPath);
        Assert.Contains("Verification failed", vm.ReportSummary);
        Assert.False(vm.IsBusy);
    }

    private sealed class DelayedStore(IRunStore inner, string delayedId) : IRunStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailLookup { get; init; }
        public async Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
        {
            if (runId == delayedId)
            {
                if (FailLookup) throw new IOException("Store unavailable");
                Started.TrySetResult();
                await Release.Task;
            }
            return await inner.LoadAsync(runId, cancellationToken);
        }
        public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default) => inner.SaveAsync(run, cancellationToken);
        public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public string GetRunDirectory(string runId) => inner.GetRunDirectory(runId);
    }

    [Fact]
    public async Task Save_copy_cannot_overwrite_another_managed_issued_revision()
    {
        var managed = Path.Combine(_root, "runs");
        var first = Path.Combine(managed, "first", "issued", "certification", "rev1", "certification.pdf");
        var second = Path.Combine(managed, "second", "issued", "certification", "rev2", "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        await File.WriteAllBytesAsync(first, [1, 2, 3]);
        await File.WriteAllBytesAsync(second, [9, 8, 7]);
        await Assert.ThrowsAsync<IOException>(() => ReportDesktopActions.CopyToLocalPathAsync(first, second,
            managedRunsDirectory: managed));
        Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(second));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(first));
        var copy = Path.Combine(_root, "runs-copies", "saved.pdf");
        await ReportDesktopActions.CopyToLocalPathAsync(first, copy, managedRunsDirectory: managed);
        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(copy));
    }

    private async Task<(TestRunRecord, ReportPreviewViewModel, Actions, ReportAttestationService)> SetupAsync()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store, "actions");
        var settings = new AppSettings { RequireAttestationBeforeExport = true, AllowPresenceInLieuOfSigning = true };
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, settings);
        var actions = new Actions(Path.Combine(_root, "saved.pdf"));
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), attestation: service, printer: actions, desktop: actions, settings: settings);
        vm.UiScheduler = action => action();
        await vm.LoadFromPathAsync(run.Reports[0].PdfPath);
        return (run, vm, actions, service);
    }

    private static async Task<TestRunRecord> SeedAsync(FileRunStore store, string id)
    {
        var path = Path.Combine(store.GetRunDirectory(id), "certification.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.4 mock report"u8.ToArray());
        var run = new TestRunRecord { RunId = id, Reports = [new() { Kind = ReportKinds.Certification, PdfPath = path }] };
        await store.SaveAsync(run);
        return run;
    }

    private sealed class Actions(string destination) : IReportPrintService, IReportDesktopActions
    {
        public string Destination => destination;
        public bool CancelSave { get; init; }
        public List<string> Prints { get; } = [];
        public List<string> Saves { get; } = [];
        public List<string> Opens { get; } = [];
        public Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prints.Add(pdfPath);
            return Task.FromResult("Print requested.");
        }
        public async Task<string?> SaveCopyAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            if (CancelSave) return null;
            await ReportDesktopActions.CopyToLocalPathAsync(pdfPath, destination, cancellationToken);
            Saves.Add(pdfPath);
            return destination;
        }
        public Task OpenInViewerAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Opens.Add(pdfPath);
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedAttestation : IReportAttestationService
    {
        public ReportAttestationResult Result { get; init; } = new() { Succeeded = true };
        public TimeSpan PresenceTimeout => TimeSpan.FromSeconds(20);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? CapturedRunId { get; private set; }
        public bool NeedsAttestation(TestRunRecord run, string reportKind) => true;
        public bool HasValidAttestation(TestRunRecord run, string reportKind) => false;
        public async Task<ReportAttestationResult> AttestAsync(TestRunRecord run, string reportKind, OperatorCredential? credential = null,
            string? pin = null, bool skipSigning = false, CancellationToken cancellationToken = default)
        {
            CapturedRunId = run.RunId;
            Started.TrySetResult();
            await Release.Task;
            // Simulate a provider completing late despite cancellation; it must never resume the old action.
            return Result;
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
