using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.Reporting;
using HardwareTest.UiThreading;
using PDFtoImage;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using SkiaSharp;

namespace HardwareTest.Features.ReportPreview;

public partial class ReportPreviewViewModel : ReactiveObject
{
    private static readonly object PdfGate = new();
    private readonly IRunStore _runStore;
    private readonly IReportService _reportService;
    private readonly OperatorSession? _operatorSession;
    private readonly IReportAttestationService? _attestation;
    private readonly IReportPrintService? _printer;
    private readonly IReportDesktopActions? _desktop;
    private readonly AppSettings? _settings;
    private readonly object _selectionLock = new();
    private bool _selectionLoading;
    private bool _actionInProgress;
    internal Func<string, List<Bitmap>>? PreviewRenderer { get; set; }
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private CancellationTokenSource _selectionCancellation = new();
    private CancellationTokenSource? _signingCancellation;
    private long _selectionVersion;
    private PendingAction? _pending;
    private OperatorCredential? _capturedCredential;
    private enum ActionKind { Sign, Save, Print, Open }
    private sealed record PendingAction(TestRunRecord Run, string Kind, ActionKind Action, long Version);
    private sealed record LoadedSelection(long Version, CancellationToken Token);
    private sealed record SigningOperation(PendingAction Pending, CancellationToken Token, string? Pin, OperatorCredential? Credential);


    /// Test seam: routes UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    public ReportPreviewViewModel(
        IRunStore runStore,
        IReportService reportService,
        OperatorSession? operatorSession = null,
        IReportAttestationService? attestation = null,
        IReportPrintService? printer = null,
        IReportDesktopActions? desktop = null,
        AppSettings? settings = null)
    {
        _runStore = runStore;
        _reportService = reportService;
        _operatorSession = operatorSession;
        _attestation = attestation;
        _printer = printer;
        _desktop = desktop;
        _settings = settings;
        Pages = [];
        Status = "Select a run PDF to preview.";

        LoadLatestCommand = ReactiveCommand.CreateFromTask(LoadLatestAsync);
        PrintCommand = ReactiveCommand.CreateFromTask(() => RequestActionAsync(ActionKind.Print));
        SignCommand = ReactiveCommand.CreateFromTask(() => RequestActionAsync(ActionKind.Sign));
        SaveCopyCommand = ReactiveCommand.CreateFromTask(() => RequestActionAsync(ActionKind.Save));
        OpenInViewerCommand = ReactiveCommand.CreateFromTask(() => RequestActionAsync(ActionKind.Open));
        SignAndContinueCommand = ReactiveCommand.CreateFromTask(() => CompleteSigningAsync(false));
        UsePresenceCommand = ReactiveCommand.CreateFromTask(() => CompleteSigningAsync(true));
        CancelSigningCommand = ReactiveCommand.Create(CancelPendingAction);
        NavigateToResultsCommand = ReactiveCommand.Create(
            () => NavigateToResultsRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<Bitmap> Pages { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> LoadLatestCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> PrintCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> NavigateToResultsCommand { get; }

    /// Raised when the operator wants to open Results to pick a PDF.
    public event EventHandler? NavigateToResultsRequested;

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SignCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SaveCopyCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> OpenInViewerCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> SignAndContinueCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> UsePresenceCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CancelSigningCommand { get; }
    [Reactive] private bool _showSigningPrompt;
    [Reactive] private bool _showSigningPin;
    [Reactive] private string _signingPin = string.Empty;
    [Reactive] private string _signingPromptStatus = string.Empty;
    [Reactive] private string _signButtonLabel = "Sign report";
    [Reactive] private string _reportSummary = "Unsigned";
    public bool AllowPresenceFallback => _settings?.AllowPresenceInLieuOfSigning == true;

    [Reactive] private string? _pdfPath;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isBusy;

    public bool ShowEmptyState => Pages.Count == 0 && !IsBusy;

    private Task RunOnUiAsync(Action action) => UiDispatch.RunAsync(action, UiScheduler);

    public Task LoadFromPathAsync(string path) => LoadFromPathCoreAsync(path, null);

    private async Task<LoadedSelection?> LoadFromPathCoreAsync(string path, long? expectedVersion)
    {
        LoadedSelection? selection = null;
        await RunOnUiAsync(() =>
        {
            var resetVersion = ResetSelection(expectedVersion, loading: true);
            if (resetVersion is null) return;
            lock (_selectionLock)
            {
                if (resetVersion != _selectionVersion) return;
                selection = new LoadedSelection(resetVersion.Value, _selectionCancellation.Token);
            }
            IsBusy = true;
            PdfPath = path;
            ReportSummary = "Loading report…";
            Status = "Loading report…";
            // Dispose any previously rendered bitmaps before clearing the collection to avoid leaks.
            foreach (var bitmap in Pages)
            {
                bitmap.Dispose();
            }

            Pages.Clear();
            this.RaisePropertyChanged(nameof(ShowEmptyState));
        }).ConfigureAwait(false);
        if (selection is null) return null;
        var version = selection.Version;
        _operatorSession?.TouchActivity();
        try
        {
            string summary;
            try
            {
                var selectedRun = await Task.Run(() => FindRunForPdfAsync(path)).ConfigureAwait(false);
                summary = await Task.Run(() => DescribeReport(selectedRun, path)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                summary = "Verification failed: " + ex.Message;
            }
            if (version != _selectionVersion) return null;
            await RunOnUiAsync(() =>
            {
                if (version == _selectionVersion) ReportSummary = summary;
            }).ConfigureAwait(false);
            if (!File.Exists(path))
            {
                await RunOnUiAsync(() =>
                {
                    if (version == _selectionVersion) Status = $"File not found: {path}";
                }).ConfigureAwait(false);
                return null;
            }

            try
            {
                // PDFtoImage is desktop-only; the Avalonia shell does not ship other RIDs.
#pragma warning disable CA1416
                var bitmaps = await Task.Run(() => PreviewRenderer?.Invoke(path) ?? RenderPages(path)).ConfigureAwait(false);
#pragma warning restore CA1416
                await RunOnUiAsync(() =>
                {
                    if (version != _selectionVersion)
                    {
                        foreach (var bitmap in bitmaps) bitmap.Dispose();
                        return;
                    }
                    foreach (var bitmap in bitmaps) Pages.Add(bitmap);

                    Status = $"Previewing {path} ({Pages.Count} page(s) shown).";
                    this.RaisePropertyChanged(nameof(ShowEmptyState));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RunOnUiAsync(() =>
                {
                    if (version != _selectionVersion) return;
                    Status = $"Preview failed: {ex.Message}";
                    this.RaisePropertyChanged(nameof(ShowEmptyState));
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_selectionLock)
            {
                if (version == _selectionVersion) _selectionLoading = false;
            }
            await RunOnUiAsync(() =>
            {
                if (version == _selectionVersion) IsBusy = _actionInProgress;
                this.RaisePropertyChanged(nameof(ShowEmptyState));
            }).ConfigureAwait(false);
        }
        lock (_selectionLock)
            return selection.Version == _selectionVersion && selection.Token == _selectionCancellation.Token
                && !selection.Token.IsCancellationRequested ? selection : null;
    }

    private async Task LoadLatestAsync()
    {
        var version = ResetSelection(null, loading: true)!.Value;
        try
        {
            var runs = await _runStore.ListAsync().ConfigureAwait(false);
            var latest = runs.FirstOrDefault();
            if (latest is null)
            {
                await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "No saved runs."; }).ConfigureAwait(false);
                return;
            }

            var run = await _runStore.LoadAsync(latest.RunId).ConfigureAwait(false);
            if (run is null)
            {
                await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "Failed to load run."; }).ConfigureAwait(false);
                return;
            }
            if (version != _selectionVersion) return;

            var path = ReportAttestationService.ResolveDefaultWorkingPdfPath(run, ProgramCatalog.ResolveDefaultReportKind(run.PlanId));
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (run.IsSchemaReadOnly)
                {
                    await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "This run is read-only; its working report is unavailable."; }).ConfigureAwait(false);
                    return;
                }
                await _reportService.GenerateReportsAsync(run, ProgramCatalog.ResolveReportKinds(run.PlanId)).ConfigureAwait(false);
                path = ReportAttestationService.ResolveDefaultWorkingPdfPath(run, ProgramCatalog.ResolveDefaultReportKind(run.PlanId));
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "No working report available."; }).ConfigureAwait(false);
                return;
            }

            await LoadFromPathCoreAsync(path, version).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "Report load failed: " + ex.Message; }).ConfigureAwait(false);
        }
        finally
        {
            lock (_selectionLock)
            {
                if (version == _selectionVersion) _selectionLoading = false;
            }
            await RunOnUiAsync(() => { if (version == _selectionVersion) IsBusy = _actionInProgress; }).ConfigureAwait(false);
        }
    }

    public void CancelPendingAction() => ResetSelection(null);

    private long? ResetSelection(long? expectedVersion, bool loading = false)
    {
        long version;
        lock (_selectionLock)
        {
            if (expectedVersion is not null && expectedVersion != _selectionVersion) return null;
            version = ++_selectionVersion;
            _selectionLoading = loading;
            _selectionCancellation.Cancel();
            _selectionCancellation.Dispose();
            _selectionCancellation = new CancellationTokenSource();
            _signingCancellation?.Cancel();
            _pending = null;
            _capturedCredential = null;
        }
        UiDispatch.Post(() =>
        {
            if (version != _selectionVersion) return;
            IsBusy = loading || _actionInProgress;
            ShowSigningPrompt = false;
            ShowSigningPin = false;
            SigningPin = string.Empty;
        }, UiScheduler);
        return version;
    }

    private async Task RequestActionAsync(ActionKind action, long? expectedVersion = null, CancellationToken? expectedToken = null)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false))
        {
            await RunOnUiAsync(() => { Status = ReportCaptureGate.WaitingMessage; SigningPromptStatus = Status; }).ConfigureAwait(false);
            return;
        }
        long version;
        CancellationToken token;
        string? path;
        lock (_selectionLock)
        {
            if (_selectionLoading || (expectedVersion is not null && expectedVersion != _selectionVersion)
                || (expectedToken is not null && expectedToken != _selectionCancellation.Token)) { _actionGate.Release(); return; }
            version = _selectionVersion;
            token = _selectionCancellation.Token;
            path = PdfPath;
        }
        var selectedPath = path;
        try
        {
            await BeginActionAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await RunOnUiAsync(() => { if (version == _selectionVersion) Status = "No PDF selected."; }).ConfigureAwait(false);
                return;
            }
            var run = await Task.Run(() => FindRunForPdfAsync(path)).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            if (action == ActionKind.Print && run is not null)
                path = ReportAttestationService.ResolvePrintOrExportPdfPath(run, path);
            var artifact = run?.Reports.FirstOrDefault(r => ReportAttestationService.PathEquals(r.PdfPath, path));
            var kind = run is null ? ReportKinds.Status : ReportAttestationService.KindForPdf(run, path);
            var issued = artifact is not null && ReportArtifactRoles.IsIssued(artifact.Role);
            var valid = run is not null && issued && _attestation is not null
                && await Task.Run(() => _attestation.HasValidAttestationForPdf(run, kind, path), token).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            var needsSignature = action == ActionKind.Sign || (action is ActionKind.Save or ActionKind.Print
                && run is not null && _attestation?.NeedsAttestation(run, kind) == true && !valid);
            if (needsSignature)
            {
                if (run is null || _attestation is null || run.IsSchemaReadOnly || issued)
                {
                    await RunOnUiAsync(() =>
                    {
                        if (version != _selectionVersion || token.IsCancellationRequested) return;
                        Status = run?.IsSchemaReadOnly == true
                        ? "This run uses a newer schema and cannot be signed."
                        : issued ? "Select a working copy to create a new signed revision. This issued revision remains unchanged."
                        : "Signing is unavailable for this PDF.";
                    }).ConfigureAwait(false);
                    return;
                }
                lock (_selectionLock)
                {
                    if (version != _selectionVersion || token.IsCancellationRequested) return;
                    _signingCancellation?.Cancel();
                    _signingCancellation?.Dispose();
                    _signingCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _pending = new PendingAction(run, kind, action, version);
                    _capturedCredential = null;
                }
                await RunOnUiAsync(() =>
                {
                    if (version != _selectionVersion) return;
                    ShowSigningPrompt = true;
                    ShowSigningPin = false;
                    SignButtonLabel = action == ActionKind.Sign ? "Sign report" : "Sign and continue";
                    SigningPromptStatus = "Tap or insert a badge to sign this report.";
                    Status = action == ActionKind.Sign ? "Sign this working report." : "Sign this report to continue the requested action.";
                }).ConfigureAwait(false);
                return;
            }
            if (action == ActionKind.Print && !ReportAttestationService.PathEquals(selectedPath, path))
            {
                var selection = await LoadFromPathCoreAsync(path, version).ConfigureAwait(false);
                if (selection is null) return;
                version = selection.Version;
                token = selection.Token;
            }
            await PerformActionAsync(action, path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (version == _selectionVersion && !token.IsCancellationRequested) Status = "Report action failed: " + ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            try { await EndActionAsync().ConfigureAwait(false); }
            finally { _actionGate.Release(); }
        }
    }

    private Task BeginActionAsync() => RunOnUiAsync(() =>
    {
        _actionInProgress = true;
        IsBusy = true;
    });

    private Task EndActionAsync() => RunOnUiAsync(() =>
    {
        _actionInProgress = false;
        IsBusy = _selectionLoading;
    });

    private async Task CompleteSigningAsync(bool presence)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false))
        {
            await RunOnUiAsync(() => { Status = ReportCaptureGate.WaitingMessage; SigningPromptStatus = Status; }).ConfigureAwait(false);
            return;
        }
        try
        {
            await BeginActionAsync().ConfigureAwait(false);
            await CompleteSigningCoreAsync(presence).ConfigureAwait(false);
        }
        finally
        {
            try { await EndActionAsync().ConfigureAwait(false); }
            finally { _actionGate.Release(); }
        }
    }

    private async Task CompleteSigningCoreAsync(bool presence)
    {
        if (_attestation is null) return;
        SigningOperation? operation = null;
        await RunOnUiAsync(() =>
        {
            lock (_selectionLock)
            {
                var pendingAction = _pending;
                var cancellation = _signingCancellation;
                if (pendingAction is null || cancellation is null || pendingAction.Version != _selectionVersion
                    || cancellation.IsCancellationRequested) return;
                operation = new SigningOperation(pendingAction, cancellation.Token,
                    ShowSigningPin ? SigningPin : null, _capturedCredential);
            }
        }).ConfigureAwait(false);
        if (operation is null) return;
        var pending = operation.Pending;
        var token = operation.Token;
        using var capture = ReportCaptureGate.TryEnter(_attestation);
        if (capture is null)
        {
            await RunOnUiAsync(() =>
            {
                if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                SigningPromptStatus = ReportCaptureGate.WaitingMessage;
                Status = SigningPromptStatus;
            }).ConfigureAwait(false);
            return;
        }
        var effectVersion = pending.Version;
        try
        {
            var result = await Task.Run(() => _attestation.AttestAsync(pending.Run, pending.Kind, operation.Credential,
                operation.Pin, presence, token), token).ConfigureAwait(false);
            // Badge capture is complete; the per-preview action gate still owns the resumed desktop operation.
            capture.Dispose();
            if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
            if (!result.Succeeded)
            {
                await RunOnUiAsync(() =>
                {
                    if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                    SigningPromptStatus = result.Message;
                    Status = result.Message;
                    if (result.PinRequired) { _capturedCredential = result.Credential; ShowSigningPin = true; }
                    SigningPin = string.Empty;
                }).ConfigureAwait(false);
                return;
            }
            var revision = result.Attestation?.RevisionId is { } revisionId
                ? pending.Run.Reports.FirstOrDefault(r => r.RevisionId == revisionId
                    && ReportArtifactRoles.IsIssued(r.Role) && string.Equals(r.Kind, pending.Kind, StringComparison.OrdinalIgnoreCase))
                : null;
            if (revision is null) throw new InvalidOperationException("Signing did not commit a report revision.");
            // Consume the pending action before previewing or invoking any external operation.
            lock (_selectionLock)
            {
                if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || token.IsCancellationRequested) return;
                _pending = null;
            }
            var selection = await LoadFromPathCoreAsync(revision.PdfPath, pending.Version).ConfigureAwait(false);
            if (selection is null) return;
            effectVersion = selection.Version;
            if (pending.Action != ActionKind.Sign)
                await PerformActionAsync(pending.Action, revision.PdfPath, selection.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (effectVersion == _selectionVersion && (effectVersion != pending.Version || !token.IsCancellationRequested))
                    Status = "Signing failed: " + ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            if (effectVersion == _selectionVersion) await RunOnUiAsync(() =>
            {
                if (effectVersion == _selectionVersion) SigningPin = string.Empty;
            }).ConfigureAwait(false);
        }
    }

    private async Task PerformActionAsync(ActionKind action, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string message;
        switch (action)
        {
            case ActionKind.Print:
                message = _printer is null ? "Printing is unavailable." : await _printer.PrintAsync(path, token).ConfigureAwait(false);
                break;
            case ActionKind.Save:
                var saved = _desktop is null ? throw new InvalidOperationException("Saving is unavailable.")
                    : await _desktop.SaveCopyAsync(path, token).ConfigureAwait(false);
                message = saved is null ? "Save cancelled." : "Saved a copy to " + saved;
                break;
            case ActionKind.Open:
                if (_desktop is null) throw new InvalidOperationException("Opening a PDF viewer is unavailable.");
                await _desktop.OpenInViewerAsync(path, token).ConfigureAwait(false);
                message = "Opened PDF in the system viewer.";
                break;
            default: return;
        }
        if (!token.IsCancellationRequested) await RunOnUiAsync(() =>
        {
            if (!token.IsCancellationRequested) Status = message;
        }).ConfigureAwait(false);
    }

    public async Task PrintFromPathAsync(string path)
    {
        var selection = await LoadFromPathCoreAsync(path, _selectionVersion).ConfigureAwait(false);
        if (selection is not null)
            await RequestActionAsync(ActionKind.Print, selection.Version, selection.Token).ConfigureAwait(false);
    }

    private string DescribeReport(TestRunRecord? run, string path)
    {
        var artifact = run?.Reports.FirstOrDefault(r => ReportAttestationService.PathEquals(r.PdfPath, path));
        if (run is null || artifact is null || !ReportArtifactRoles.IsIssued(artifact.Role)) return "Unsigned · working copy";
        var stamp = ReportAttestationService.FindForArtifact(run, artifact);
        var label = _attestation?.HasValidAttestationForPdf(run, artifact.Kind, path) == true
            ? stamp?.Kind == AttestationKind.Signed ? stamp.Algorithm == AttestationAlgorithm.MockHmac ? "Digitally signed (mock)" : "Digitally signed" : "Presence attested"
            : "Verification failed";
        return $"{label} · {stamp?.DisplayName ?? "Unknown signer"} · {stamp?.CapturedAt:u} · revision {artifact.RevisionNumber}";
    }

    private async Task<TestRunRecord?> FindRunForPdfAsync(string pdfPath)
    {
        var runId = ReportAttestationService.GuessRunIdFromPdfPath(pdfPath);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            var byFolder = await _runStore.LoadAsync(runId).ConfigureAwait(false);
            if (byFolder is not null && ReportAttestationService.RunOwnsPdf(byFolder, pdfPath))
            {
                return byFolder;
            }
        }

        foreach (var summary in await _runStore.ListAsync().ConfigureAwait(true))
        {
            var loaded = await _runStore.LoadAsync(summary.RunId).ConfigureAwait(false);
            if (loaded is not null && ReportAttestationService.RunOwnsPdf(loaded, pdfPath))
            {
                return loaded;
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static List<Bitmap> RenderPages(string path)
    {
        lock (PdfGate)
        {
            using var input = File.OpenRead(path);
            var images = Conversion.ToImages(input).Take(10).ToList();
            var result = new List<Bitmap>(images.Count);
            foreach (var skBitmap in images)
            {
                result.Add(ToAvaloniaBitmap(skBitmap));
                skBitmap.Dispose();
            }

            return result;
        }
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = data.AsStream();
        return new Bitmap(stream);
    }
}
