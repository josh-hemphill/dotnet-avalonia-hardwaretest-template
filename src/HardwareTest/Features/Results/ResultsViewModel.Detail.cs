using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
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
    public ObservableCollection<DutHistoryMetricRow> HistoryMetrics { get; }
    public ObservableCollection<RunReportItemViewModel> ReportItems { get; }
    public ObservableCollection<ExportTarget> ExportTargets { get; }
    public ObservableCollection<string> ResultFilterOptions { get; }
    public ObservableCollection<string> PlanFilterOptions { get; }
    public ObservableCollection<string> DutFilterOptions { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenDefaultReportCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ReprintCommand { get; }
    public ReactiveCommand<RunReportItemViewModel?, System.Reactive.Unit> OpenReportCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ExportPackageCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CaptureAttestationCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> UsePresenceAttestationCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CancelAttestationCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CloseDetailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToRunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearFailedStepsFilterCommand { get; }

    [Reactive] private TestRunSummary? _selectedRun;
    [Reactive] private TestRunRecord? _openedRun;
    [Reactive] private bool _showDetail;
    [Reactive] private string _status = "Loading runs…";
    [Reactive] private string _historySummary = string.Empty;
    [Reactive] private string _historySeverity = string.Empty;
    [Reactive] private bool _hasHistory;
    [Reactive] private bool _hasPresentationTiles;
    [Reactive] private bool _hasReports;
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

    private void ApplyOpenedRun(TestRunRecord? opened)
    {
        OpenedRun = opened;
        SampleDetails.Clear();
        PresentationTiles.Clear();
        HasPresentationTiles = false;
        ClearComparison();
        HistorySummary = string.Empty;
        HistorySeverity = string.Empty;
        HasHistory = false;
        HistoryMetrics.Clear();
        ReportItems.Clear();
        HasReports = false;
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

        foreach (var tile in PresentationRoleMap.BuildFromStoredSamples(OpenedRun.Samples))
        {
            PresentationTiles.Add(tile);
        }

        HasPresentationTiles = PresentationTiles.Count > 0;
        LoadReportItems(OpenedRun);

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
            foreach (var artifact in run.Reports)
            {
                ReportItems.Add(new RunReportItemViewModel
                {
                    Kind = artifact.Kind,
                    Title = string.IsNullOrWhiteSpace(artifact.Title) ? artifact.Kind : artifact.Title,
                    PdfPath = artifact.PdfPath,
                    GeneratedAtText = artifact.GeneratedAt.ToString("u", CultureInfo.InvariantCulture),
                    IsDefault = string.Equals(artifact.Kind, defaultKind, StringComparison.OrdinalIgnoreCase),
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
                IsDefault = true,
            });
        }

        HasReports = ReportItems.Count > 0;
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

        var defaultKind = ProgramCatalog.ResolveDefaultReportKind(run.PlanId);
        if (!TryBeginCertifiedAction(run, defaultKind, PendingOpenDefault))
        {
            return;
        }

        ReportOpened?.Invoke(this, path);
        Status = $"Opened default report ({ProgramCatalog.ResolveDefaultReportKind(run.PlanId)}).";
    }

    /// Picks the catalog default kind's PDF, else status, else ReportPdfPath, else first artifact.
    public static string? ResolveDefaultReportPath(TestRunRecord run)
    {
        var defaultKind = ProgramCatalog.ResolveDefaultReportKind(run.PlanId);
        var byKind = run.Reports.FirstOrDefault(r =>
            string.Equals(r.Kind, defaultKind, StringComparison.OrdinalIgnoreCase));
        if (byKind is not null && !string.IsNullOrWhiteSpace(byKind.PdfPath))
        {
            return byKind.PdfPath;
        }

        var status = run.Reports.FirstOrDefault(r =>
            string.Equals(r.Kind, ReportKinds.Status, StringComparison.OrdinalIgnoreCase));
        if (status is not null && !string.IsNullOrWhiteSpace(status.PdfPath))
        {
            return status.PdfPath;
        }

        if (!string.IsNullOrWhiteSpace(run.ReportPdfPath))
        {
            return run.ReportPdfPath;
        }

        return run.Reports.FirstOrDefault()?.PdfPath;
    }

    private Task OpenReportAsync(RunReportItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.PdfPath) || !File.Exists(item.PdfPath))
        {
            Status = "Report PDF not found.";
            return Task.CompletedTask;
        }

        if (OpenedRun is not null && !TryBeginCertifiedAction(OpenedRun, item.Kind, PendingOpenItem, item))
        {
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

                var kinds = run.Reports.Count > 0
                    ? run.Reports.Select(r => r.Kind).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : ProgramCatalog.ResolveReportKinds(run.PlanId);
                var artifacts = await _reportService.GenerateReportsAsync(run, kinds, history);
                OpenedRun = run;
                LoadReportItems(run);
                Status = $"Regenerated {artifacts.Count} report(s).";
                if (!TryBeginCertifiedAction(run, ReportKinds.Certification, PendingReprintOpen))
                {
                    return;
                }

                var primary = run.ReportPdfPath ?? artifacts.FirstOrDefault()?.PdfPath;
                if (primary is not null)
                {
                    ReportOpened?.Invoke(this, primary);
                }
            }
            catch (Exception ex)
            {
                Status = $"Reprint failed: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
            _busyGate.Release();
        }
    }
}
