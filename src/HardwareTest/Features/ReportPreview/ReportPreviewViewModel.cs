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
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private CancellationTokenSource _selectionCancellation = new();
    private CancellationTokenSource? _signingCancellation;
    private long _selectionVersion;
    private PendingAction? _pending;
    private OperatorCredential? _capturedCredential;
    private bool _capturing;
    private enum ActionKind { Sign, Save, Print, Open }
    private sealed record PendingAction(TestRunRecord Run, string Kind, string PdfPath, string? RevisionId, ActionKind Action, long Version);


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

    private async Task LoadFromPathCoreAsync(string path, long? expectedVersion)
    {
        var resetVersion = ResetSelection(expectedVersion);
        if (resetVersion is null) return;
        var version = resetVersion.Value;
        _operatorSession?.TouchActivity();
        var selectedRun = await Task.Run(() => FindRunForPdfAsync(path)).ConfigureAwait(false);
        var summary = await Task.Run(() => DescribeReport(selectedRun, path)).ConfigureAwait(false);
        if (version != _selectionVersion) return;
        await RunOnUiAsync(() =>
        {
            if (version != _selectionVersion) return;
            IsBusy = true;
            PdfPath = path;
            ReportSummary = summary;
            // Dispose any previously rendered bitmaps before clearing the collection to avoid leaks.
            foreach (var bitmap in Pages)
            {
                bitmap.Dispose();
            }

            Pages.Clear();
            this.RaisePropertyChanged(nameof(ShowEmptyState));
        }).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                await RunOnUiAsync(() => Status = $"File not found: {path}").ConfigureAwait(false);
                return;
            }

            try
            {
                // PDFtoImage is desktop-only; the Avalonia shell does not ship other RIDs.
#pragma warning disable CA1416
                var bitmaps = await Task.Run(() => RenderPages(path)).ConfigureAwait(false);
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
                    Status = $"Preview failed: {ex.Message}";
                    this.RaisePropertyChanged(nameof(ShowEmptyState));
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                if (version == _selectionVersion) IsBusy = false;
                this.RaisePropertyChanged(nameof(ShowEmptyState));
            }).ConfigureAwait(false);
        }
    }

    private async Task LoadLatestAsync()
    {
        var runs = await _runStore.ListAsync().ConfigureAwait(false);
        var latest = runs.FirstOrDefault();
        if (latest is null)
        {
            await RunOnUiAsync(() => Status = "No saved runs.").ConfigureAwait(false);
            return;
        }

        var run = await _runStore.LoadAsync(latest.RunId).ConfigureAwait(false);
        if (run is null)
        {
            await RunOnUiAsync(() => Status = "Failed to load run.").ConfigureAwait(false);
            return;
        }

        var path = run.ReportPdfPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            path = await _reportService.GeneratePdfAsync(run).ConfigureAwait(false);
        }

        await LoadFromPathAsync(path).ConfigureAwait(false);
    }

    public void CancelPendingAction() => ResetSelection(null);

    private long? ResetSelection(long? expectedVersion)
    {
        long version;
        lock (_selectionLock)
        {
            if (expectedVersion is not null && expectedVersion != _selectionVersion) return null;
            version = ++_selectionVersion;
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
            ShowSigningPrompt = false;
            ShowSigningPin = false;
            SigningPin = string.Empty;
        }, UiScheduler);
        return version;
    }

    private async Task RequestActionAsync(ActionKind action, long? expectedVersion = null)
    {
        if (!await _actionGate.WaitAsync(0).ConfigureAwait(false)) return;
        long version;
        CancellationToken token;
        string? path;
        lock (_selectionLock)
        {
            if (expectedVersion is not null && expectedVersion != _selectionVersion) { _actionGate.Release(); return; }
            version = _selectionVersion;
            token = _selectionCancellation.Token;
            path = PdfPath;
        }
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await RunOnUiAsync(() => Status = "No PDF selected.").ConfigureAwait(false);
                return;
            }
            var run = await Task.Run(() => FindRunForPdfAsync(path)).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            var artifact = run?.Reports.FirstOrDefault(r => string.Equals(r.PdfPath, path, StringComparison.OrdinalIgnoreCase));
            var kind = run is null ? ReportKinds.Status : ReportAttestationService.KindForPdf(run, path);
            var issued = artifact is not null && ReportArtifactRoles.IsIssued(artifact.Role);
            var valid = run is not null && issued && _attestation is not null
                && await Task.Run(() => _attestation.HasValidAttestation(run, kind, artifact!.RevisionId), token).ConfigureAwait(false);
            if (version != _selectionVersion || token.IsCancellationRequested) return;
            var needsSignature = action == ActionKind.Sign || (action is ActionKind.Save or ActionKind.Print
                && run is not null && _attestation?.NeedsAttestation(run, kind) == true && !valid);
            if (needsSignature)
            {
                if (run is null || _attestation is null || run.IsSchemaReadOnly || issued)
                {
                    await RunOnUiAsync(() => Status = run?.IsSchemaReadOnly == true
                        ? "This run uses a newer schema and cannot be signed."
                        : issued ? "Select a working copy to create a new signed revision. This issued revision remains unchanged."
                        : "Signing is unavailable for this PDF.").ConfigureAwait(false);
                    return;
                }
                lock (_selectionLock)
                {
                    if (version != _selectionVersion || token.IsCancellationRequested) return;
                    _signingCancellation?.Cancel();
                    _signingCancellation?.Dispose();
                    _signingCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _pending = new PendingAction(run, kind, path, artifact?.RevisionId, action, version);
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
            await PerformActionAsync(action, path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == _selectionVersion) await RunOnUiAsync(() => Status = "Report action failed: " + ex.Message).ConfigureAwait(false);
        }
        finally { _actionGate.Release(); }
    }

    private async Task CompleteSigningAsync(bool presence)
    {
        var pending = _pending;
        var cancellation = _signingCancellation;
        if (pending is null || cancellation is null || _attestation is null || _capturing || pending.Version != _selectionVersion) return;
        _capturing = true;
        var pin = ShowSigningPin ? SigningPin : null;
        var credential = _capturedCredential;
        var effectVersion = pending.Version;
        try
        {
            var result = await Task.Run(() => _attestation.AttestAsync(pending.Run, pending.Kind, credential,
                pin, presence, cancellation.Token), cancellation.Token).ConfigureAwait(false);
            if (!ReferenceEquals(_pending, pending) || pending.Version != _selectionVersion || cancellation.IsCancellationRequested) return;
            if (!result.Succeeded)
            {
                await RunOnUiAsync(() =>
                {
                    SigningPromptStatus = result.Message;
                    Status = result.Message;
                    if (result.PinRequired) { _capturedCredential = result.Credential; ShowSigningPin = true; }
                    SigningPin = string.Empty;
                }).ConfigureAwait(false);
                return;
            }
            var revision = ReportRevisions.Latest(pending.Run, pending.Kind);
            if (revision is null) throw new InvalidOperationException("Signing did not commit a report revision.");
            // Consume the pending action before previewing or invoking any external operation.
            _pending = null;
            var nextVersion = pending.Version + 1;
            effectVersion = nextVersion;
            await LoadFromPathCoreAsync(revision.PdfPath, pending.Version).ConfigureAwait(false);
            CancellationToken resumeToken;
            lock (_selectionLock)
            {
                if (nextVersion != _selectionVersion) return;
                resumeToken = _selectionCancellation.Token;
            }
            if (pending.Action != ActionKind.Sign)
                await PerformActionAsync(pending.Action, revision.PdfPath, resumeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (effectVersion == _selectionVersion) await RunOnUiAsync(() => Status = "Signing failed: " + ex.Message).ConfigureAwait(false);
        }
        finally
        {
            _capturing = false;
            if (effectVersion == _selectionVersion) await RunOnUiAsync(() => SigningPin = string.Empty).ConfigureAwait(false);
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
        if (!token.IsCancellationRequested) await RunOnUiAsync(() => Status = message).ConfigureAwait(false);
    }

    public async Task PrintFromPathAsync(string path)
    {
        var currentVersion = _selectionVersion;
        var expectedVersion = currentVersion + 1;
        await LoadFromPathCoreAsync(path, currentVersion).ConfigureAwait(false);
        if (_selectionVersion == expectedVersion) await RequestActionAsync(ActionKind.Print, expectedVersion).ConfigureAwait(false);
    }

    private string DescribeReport(TestRunRecord? run, string path)
    {
        var artifact = run?.Reports.FirstOrDefault(r => string.Equals(r.PdfPath, path, StringComparison.OrdinalIgnoreCase));
        if (run is null || artifact is null || !ReportArtifactRoles.IsIssued(artifact.Role)) return "Unsigned · working copy";
        var stamp = run.Attestations.LastOrDefault(a => a.RevisionId == artifact.RevisionId
            && string.Equals(a.ReportKind, artifact.Kind, StringComparison.OrdinalIgnoreCase));
        var label = _attestation?.HasValidAttestation(run, artifact.Kind, artifact.RevisionId) == true
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
