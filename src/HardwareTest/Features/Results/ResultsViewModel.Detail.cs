using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Text;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using HardwareTest.UiThreading;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

/// Detail pane, default-report open, history tiles, and regenerate — not list/filter chrome.
public partial class ResultsViewModel
{
    /// Caps sidebar step/sample rows — ItemsControl is not virtualized inside the detail scroller.
    private const int SidebarDetailCap = 200;

    /// Test seam: routes UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    private void PostToUi(Action action) => UiDispatch.Post(action, UiScheduler);

    private Task RunOnUiAsync(Action action) => UiDispatch.RunAsync(action, UiScheduler);

    private void ScheduleOpenDetail()
        => OpenAsync().ContinueWith(
            t =>
            {
                if (t.Exception is null)
                {
                    return;
                }

                var msg = $"Open failed: {t.Exception.GetBaseException().Message}";
                PostToUi(() => Status = msg);
            },
            TaskScheduler.Default);

    public ObservableCollection<TestRunSummary> Runs { get; }
    public ObservableCollection<string> StepDetails { get; }
    public ObservableCollection<string> SampleDetails { get; }
    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; }
    public ObservableCollection<MeasurementEventMark> TimingEvents { get; } = [];
    public ObservableCollection<DutHistoryMetricRow> HistoryMetrics { get; }
    public ObservableCollection<RunReportItemViewModel> ReportItems { get; }
    public ObservableCollection<ExportTarget> ExportTargets { get; }
    public ObservableCollection<string> ResultFilterOptions { get; }
    public ObservableCollection<string> PlanFilterOptions { get; }
    public ObservableCollection<string> DutFilterOptions { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> OpenCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> OpenDefaultReportCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ReprintCommand { get; }
    public ReactiveCommand<RunReportItemViewModel?, ReactiveUI.Primitives.RxVoid> OpenReportCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ExportPackageCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CaptureAttestationCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> UsePresenceAttestationCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CancelAttestationCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CloseDetailCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> NavigateToRunCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ClearFailedStepsFilterCommand { get; }

    [Reactive] private TestRunSummary? _selectedRun;
    [Reactive] private TestRunRecord? _openedRun;
    [Reactive] private bool _showDetail;
    [Reactive] private string _status = "Loading runs…";
    [Reactive] private string _historySummary = string.Empty;
    [Reactive] private string _historySeverity = string.Empty;
    [Reactive] private bool _hasHistory;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private bool _hasTimingStrip;
    [Reactive] private double _timingDurationSec;
    [Reactive] private IReadOnlyList<(double T0, double T1)> _timingSpans = [];
    [Reactive] private bool _hasReports;
    [Reactive] private bool _hasAttestation;
    [Reactive] private string _attestationSummary = string.Empty;
    [Reactive] private string _searchText = string.Empty;
    [Reactive] private string _resultFilter = AllFilter;
    [Reactive] private string _planFilter = AllFilter;
    [Reactive] private string _dutFilter = AllFilter;
    [Reactive] private string _filterStatus = string.Empty;
    [Reactive] private string _schemaBadge = string.Empty;
    [Reactive] private bool _hasSchemaBadge;
    [Reactive] private string _schemaWarning = string.Empty;
    [Reactive] private bool _hasSchemaWarning;
    [Reactive] private ExportTarget? _selectedExportTarget;
    [Reactive] private bool _hasExportTargets;
    [Reactive] private bool _isBusy;

    public event EventHandler<string>? ReportOpened;
    public event EventHandler<string>? CertifiedPrintReady;

    public bool HasRuns => Runs.Count > 0;

    public bool HasSchemaDrift => _allRuns.Any(r => r.IsSchemaReadOnly);

    public string SchemaDriftSummary =>
        HasSchemaDrift
            ? "One or more runs used a newer schema and are read-only on this app."
            : string.Empty;

    /// Raised when the operator wants to navigate to the Run page from the empty state.
    public event EventHandler? NavigateToRunRequested;

    public async Task OpenSelectedRunAsync() => await OpenAsync();

    /// Opens the catalog-configured default PDF for the selected/opened run (double-click).
    public Task OpenSelectedRunDefaultReportAsync() => OpenDefaultReportAsync();

    public Task LoadRunsAsync() => RefreshAsync();

    /// Reloads the run list and selects the matching run when present.
    public async Task OpenRunByIdAsync(string? runId)
    {
        await RefreshAsync();
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        var stored = _allRuns.FirstOrDefault(r =>
            string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        if (stored is null)
        {
            Status = $"Run '{ShortId.Display(runId)}' not found in history.";
            return;
        }

        if (stored.Result == RunResult.Failed)
        {
            ResultFilter = nameof(RunResult.Failed);
            ApplyFilters();
        }

        var match = Runs.FirstOrDefault(r =>
            string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            WidenFiltersForHiddenRun(stored.Result == RunResult.Failed);
            match = Runs.FirstOrDefault(r =>
                string.Equals(r.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        if (match is not null)
        {
            _suppressAutoOpen = true;
            try
            {
                SelectedRun = match;
            }
            finally
            {
                _suppressAutoOpen = false;
            }

            await OpenAsync().ConfigureAwait(false);
            Status = $"Opened run {ShortId.Display(runId)}.";
        }
        else
        {
            Status = $"Run '{ShortId.Display(runId)}' not found in history.";
        }
    }

    private async Task RefreshAsync()
    {
        await _busyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
            _operatorSession?.TouchActivity();
            var listed = await _runStore.ListAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _allRuns.Clear();
                foreach (var run in listed)
                {
                    _allRuns.Add(run);
                }

                RebuildFilterOptions();
                ApplyFilters();
                Status = _allRuns.Count == 0 ? "No runs yet." : $"Loaded {_allRuns.Count} run(s).";
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
            _busyGate.Release();
        }
    }

    private async Task OpenAsync()
    {
        if (SelectedRun is null)
        {
            await RunOnUiAsync(() => Status = "Select a run first.").ConfigureAwait(false);
            return;
        }

        var runId = SelectedRun.RunId;
        var opened = await _runStore.LoadAsync(runId).ConfigureAwait(false);
        await RunOnUiAsync(() => ApplyOpenedRun(opened)).ConfigureAwait(false);
        if (opened is null)
        {
            return;
        }

        if (_comparison is not null)
        {
            var comparison = await _comparison.CompareToPreviousAsync(opened).ConfigureAwait(false);
            await RunOnUiAsync(() => ApplyComparison(comparison)).ConfigureAwait(false);
        }

        if (_dutHistory is null)
        {
            return;
        }

        var report = await _dutHistory.AnalyzeAsync(opened).ConfigureAwait(false);
        await RunOnUiAsync(() => ApplyDutHistory(report)).ConfigureAwait(false);
    }

    private void ClearTimingPresentation()
    {
        TimingEvents.Clear();
        HasTimingStrip = false;
        TimingDurationSec = 0;
        TimingSpans = [];
    }

    private void ApplyOpenedRun(TestRunRecord? opened)
    {
        OpenedRun = opened;
        SampleDetails.Clear();
        PresentationTiles.Clear();
        HasPresentationTiles = false;
        ClearTimingPresentation();
        ClearComparison();
        HistorySummary = string.Empty;
        HistorySeverity = string.Empty;
        HasHistory = false;
        HistoryMetrics.Clear();
        ReportItems.Clear();
        HasReports = false;
        HasAttestation = false;
        AttestationSummary = string.Empty;
        SchemaBadge = string.Empty;
        HasSchemaBadge = false;
        SchemaWarning = string.Empty;
        HasSchemaWarning = false;
        if (OpenedRun is null)
        {
            ShowDetail = false;
            Status = "Run not found.";
            RebuildStepDetails();
            return;
        }

        ShowDetail = true;
        if (OpenedRun.IsSchemaReadOnly)
        {
            SchemaBadge = "Read-only";
            HasSchemaBadge = true;
            SchemaWarning =
                $"Schema {OpenedRun.StoredSchemaVersion} is newer than this app ({HardwareTest.Core.Serialization.SchemaVersions.TestRunRecord}). "
                + $"Written by {OpenedRun.AppVersion ?? "unknown"}.";
            HasSchemaWarning = true;
        }
        else if (OpenedRun.IsLegacy)
        {
            SchemaBadge = "Legacy";
            HasSchemaBadge = true;
        }

        ShowFailedStepsOnly = OpenedRun.Result == RunResult.Failed;
        RebuildStepDetails();

        foreach (var sample in OpenedRun.Samples.Take(SidebarDetailCap))
        {
            SampleDetails.Add(sample.ToDisplayLine());
        }

        if (OpenedRun.Samples.Count > SidebarDetailCap)
        {
            SampleDetails.Add($"…and {OpenedRun.Samples.Count - SidebarDetailCap} more samples (see run.json / report).");
        }

        var marks = SeriesTimingChrome.ToMarks(OpenedRun.Events);
        foreach (var mark in marks)
        {
            TimingEvents.Add(mark);
        }

        foreach (var tile in PresentationRoleMap.BuildFromStoredSamples(OpenedRun.Samples))
        {
            if (tile.IsChart && tile.UsesTimeAxis)
            {
                tile.SetTimingChrome(
                    marks,
                    SeriesTimingChrome.OutOfBandSpans(
                        tile.Xs,
                        tile.Ys,
                        tile.YsLength,
                        tile.LimitLow,
                        tile.LimitHigh));
            }

            PresentationTiles.Add(tile);
        }

        HasPresentationTiles = PresentationTiles.Any(t => !t.IsStrip);
        var chart = PresentationTiles.FirstOrDefault(t => t.IsChart && t.UsesTimeAxis);
        TimingSpans = chart?.OutOfBandSpans ?? [];
        TimingDurationSec = SeriesTimingChrome.StripDurationSec(
            TimingEvents,
            chart is { YsLength: > 0 } ? chart.Xs[chart.YsLength - 1] : null);
        HasTimingStrip = TimingEvents.Count > 0 || TimingSpans.Count > 0 || PresentationTiles.Any(t => t.IsStrip);
        LoadReportItems(OpenedRun);
        LoadAttestation(OpenedRun);

        Status = $"Opened {ShortId.Display(OpenedRun.RunId)} ({OpenedRun.Result}) — {OpenedRun.Steps.Count} steps, {OpenedRun.Samples.Count} samples."
                 + (OpenedRun.SessionId is { } sid ? $" Session {ShortId.Display(sid)}." : string.Empty);
        if (!string.IsNullOrWhiteSpace(TriageSummary))
        {
            Status += " " + TriageSummary;
        }
        if (!string.IsNullOrWhiteSpace(SchemaWarning))
        {
            Status += " " + SchemaWarning;
        }

        // Detail pane only — default PDF is opened via double-click / OpenDefaultReportCommand.
    }

    private void ApplyDutHistory(DutHistoryReport report)
    {
        HistorySummary = report.OperatorSummary;
        HistorySeverity = report.OverallSeverity.ToString();
        HasHistory = !string.IsNullOrWhiteSpace(report.OperatorSummary);
        HistoryMetrics.Clear();
        foreach (var metric in report.Metrics)
        {
            HistoryMetrics.Add(new DutHistoryMetricRow
            {
                Channel = metric.Channel,
                CurrentMeanText = metric.CurrentMean.ToString("G6", CultureInfo.InvariantCulture),
                PriorMeanText = metric.PriorMean?.ToString("G6", CultureInfo.InvariantCulture) ?? "—",
                PercentDeltaText = metric.PercentDelta is { } d
                    ? d.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                    : "—",
                Severity = metric.Severity.ToString(),
            });
        }
    }

    private void LoadReportItems(TestRunRecord run)
    {
        ReportItems.Clear();
        var defaultKind = ProgramCatalog.ResolveDefaultReportKind(run.PlanId);
        if (run.Reports.Count > 0)
        {
            foreach (var artifact in run.Reports
                         .OrderBy(a => a.Kind, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(a => ReportArtifactRoles.IsIssued(a.Role) ? 1 : 0))
            {
                var issued = ReportArtifactRoles.IsIssued(artifact.Role);
                ReportItems.Add(new RunReportItemViewModel
                {
                    Kind = artifact.Kind,
                    Title = string.IsNullOrWhiteSpace(artifact.Title) ? artifact.Kind : artifact.Title,
                    PdfPath = artifact.PdfPath,
                    GeneratedAtText = artifact.GeneratedAt.ToString("u", CultureInfo.InvariantCulture),
                    Role = issued ? ReportArtifactRoles.Issued : ReportArtifactRoles.Working,
                    RoleLabel = issued ? "Issued" : "Working",
                    IsIssued = issued,
                    IsDefault = !issued
                                && string.Equals(artifact.Kind, defaultKind, StringComparison.OrdinalIgnoreCase),
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(run.ReportPdfPath))
        {
            ReportItems.Add(new RunReportItemViewModel
            {
                Kind = ReportKinds.Status,
                Title = "Status Report",
                PdfPath = run.ReportPdfPath!,
                GeneratedAtText = string.Empty,
                Role = ReportArtifactRoles.Working,
                RoleLabel = "Working",
                IsDefault = true,
            });
        }

        HasReports = ReportItems.Count > 0;
    }

    private void LoadAttestation(TestRunRecord run)
    {
        var attestation = ReportAttestationService.Find(run, ReportKinds.Certification);
        HasAttestation = attestation is not null;
        AttestationSummary = attestation is null ? string.Empty : ReportAttestationStamp.FormatSummary(attestation);
    }

    private async Task OpenDefaultReportAsync()
    {
        var run = OpenedRun;
        if (run is null && SelectedRun is not null)
        {
            run = await _runStore.LoadAsync(SelectedRun.RunId);
        }

        if (run is null)
        {
            Status = "Select a run first.";
            return;
        }

        var path = ResolveDefaultReportPath(run);
        if (path is null || !File.Exists(path))
        {
            Status = "Default report PDF not found.";
            return;
        }

        ReportOpened?.Invoke(this, path);
        Status = $"Opened default report ({ProgramCatalog.ResolveDefaultReportKind(run.PlanId)}).";
    }

    /// Picks the catalog default kind's working PDF, else status, else ReportPdfPath, else first working artifact.
    public static string? ResolveDefaultReportPath(TestRunRecord run)
    {
        var defaultKind = ProgramCatalog.ResolveDefaultReportKind(run.PlanId);
        var byKind = ReportAttestationService.ResolveWorkingPdfPath(run, defaultKind);
        if (!string.IsNullOrWhiteSpace(byKind))
        {
            return byKind;
        }

        var status = ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Status);
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        if (!string.IsNullOrWhiteSpace(run.ReportPdfPath))
        {
            return run.ReportPdfPath;
        }

        return run.Reports.FirstOrDefault(r => ReportArtifactRoles.IsWorking(r.Role))?.PdfPath
               ?? run.Reports.FirstOrDefault()?.PdfPath;
    }

    private Task OpenReportAsync(RunReportItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.PdfPath) || !File.Exists(item.PdfPath))
        {
            Status = "Report PDF not found.";
            return Task.CompletedTask;
        }

        ReportOpened?.Invoke(this, item.PdfPath);
        Status = $"Opened {item.Title}.";
        return Task.CompletedTask;
    }

    private async Task ReprintAsync()
    {
        if (SelectedRun is null)
        {
            Status = "Select a run first.";
            return;
        }

        await _busyGate.WaitAsync().ConfigureAwait(false);
        IsBusy = true;
        try
        {
            var run = await _runStore.LoadAsync(SelectedRun.RunId);
            if (run is null)
            {
                Status = "Run not found.";
                return;
            }

            try
            {
                DutHistoryReport? history = null;
                if (_dutHistory is not null)
                {
                    history = await _dutHistory.AnalyzeAsync(run);
                }

                IReadOnlyList<string> kinds = run.Reports
                    .Where(r => ReportArtifactRoles.IsWorking(r.Role))
                    .Select(r => r.Kind)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (kinds.Count == 0)
                {
                    kinds = run.Reports.Count > 0
                        ? run.Reports.Select(r => r.Kind).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : ProgramCatalog.ResolveReportKinds(run.PlanId);
                }
                var artifacts = await _reportService.GenerateReportsAsync(run, kinds, history);
                OpenedRun = run;
                LoadReportItems(run);
                LoadAttestation(run);
                Status = $"Regenerated {artifacts.Count} report(s).";
                var primary = run.ReportPdfPath ?? artifacts.FirstOrDefault()?.PdfPath;
                if (primary is not null)
                {
                    ReportOpened?.Invoke(this, primary);
                }
            }
            catch (Exception ex)
            {
                Status = $"Regenerate failed: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
            _busyGate.Release();
        }
    }
}
