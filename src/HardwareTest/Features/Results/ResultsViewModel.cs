using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public sealed class DutHistoryMetricRow
{
    public required string Channel { get; init; }
    public required string CurrentMeanText { get; init; }
    public required string PriorMeanText { get; init; }
    public required string PercentDeltaText { get; init; }
    public required string Severity { get; init; }
}

public sealed class RunReportItemViewModel
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string PdfPath { get; init; }
    public required string GeneratedAtText { get; init; }
    public bool IsDefault { get; init; }
}

public partial class ResultsViewModel : ReactiveObject
{
    public const string AllFilter = "All";
    public const string NoneDutFilter = "(none)";
    private readonly IRunStore _runStore;
    private readonly IReportService _reportService;
    private readonly IDutHistoryService? _dutHistory;
    private readonly List<TestRunSummary> _allRuns = [];

    public ResultsViewModel(
        IRunStore runStore,
        IReportService reportService,
        IDutHistoryService? dutHistory = null)
    {
        _runStore = runStore;
        _reportService = reportService;
        _dutHistory = dutHistory;
        Runs = [];
        StepDetails = [];
        SampleDetails = [];
        PresentationTiles = [];
        HistoryMetrics = [];
        ReportItems = [];
        ResultFilterOptions = [AllFilter, nameof(RunResult.Passed), nameof(RunResult.Failed), "Other"];
        PlanFilterOptions = [AllFilter];
        DutFilterOptions = [AllFilter];
        ResultFilter = AllFilter;
        PlanFilter = AllFilter;
        DutFilter = AllFilter;
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        OpenDefaultReportCommand = ReactiveCommand.CreateFromTask(OpenDefaultReportAsync);
        ReprintCommand = ReactiveCommand.CreateFromTask(ReprintAsync);
        OpenReportCommand = ReactiveCommand.Create<RunReportItemViewModel?>(OpenReport);
        CloseDetailCommand = ReactiveCommand.Create(() =>
        {
            ShowDetail = false;
            HistorySummary = string.Empty;
            HistorySeverity = string.Empty;
            HasHistory = false;
            HistoryMetrics.Clear();
            PresentationTiles.Clear();
            HasPresentationTiles = false;
            ReportItems.Clear();
            HasReports = false;
            SchemaBadge = string.Empty;
            HasSchemaBadge = false;
            SchemaWarning = string.Empty;
            HasSchemaWarning = false;
        });

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchText) or nameof(ResultFilter)
                or nameof(PlanFilter) or nameof(DutFilter))
            {
                ApplyFilters();
            }
            else if (args.PropertyName == nameof(SelectedRun) && SelectedRun is not null)
            {
                ScheduleOpenDetail();
            }
        };
    }

    private void ScheduleOpenDetail()
        => OpenAsync().ContinueWith(
            t =>
            {
                if (t.Exception is not null)
                {
                    Status = $"Open failed: {t.Exception.GetBaseException().Message}";
                }
            },
            TaskScheduler.Default);

    public ObservableCollection<TestRunSummary> Runs { get; }
    public ObservableCollection<string> StepDetails { get; }
    public ObservableCollection<string> SampleDetails { get; }
    public ObservableCollection<PresentationTileViewModel> PresentationTiles { get; }
    public ObservableCollection<DutHistoryMetricRow> HistoryMetrics { get; }
    public ObservableCollection<RunReportItemViewModel> ReportItems { get; }
    public ObservableCollection<string> ResultFilterOptions { get; }
    public ObservableCollection<string> PlanFilterOptions { get; }
    public ObservableCollection<string> DutFilterOptions { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenDefaultReportCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ReprintCommand { get; }
    public ReactiveCommand<RunReportItemViewModel?, System.Reactive.Unit> OpenReportCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CloseDetailCommand { get; }

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

    public event EventHandler<string>? ReportOpened;

    public async Task OpenSelectedRunAsync() => await OpenAsync();

    /// Opens the catalog-configured default PDF for the selected/opened run (double-click).
    public Task OpenSelectedRunDefaultReportAsync() => OpenDefaultReportAsync();

    public Task LoadRunsAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        _allRuns.Clear();
        foreach (var run in await _runStore.ListAsync())
        {
            _allRuns.Add(run);
        }

        RebuildFilterOptions();
        ApplyFilters();
        Status = _allRuns.Count == 0 ? "No runs yet." : $"Loaded {_allRuns.Count} run(s).";
    }

    private void RebuildFilterOptions()
    {
        var plans = _allRuns
            .Select(r => string.IsNullOrWhiteSpace(r.PlanName) ? r.PlanId : r.PlanName)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        PlanFilterOptions.Clear();
        PlanFilterOptions.Add(AllFilter);
        foreach (var plan in plans)
        {
            PlanFilterOptions.Add(plan);
        }

        if (!PlanFilterOptions.Contains(PlanFilter))
        {
            PlanFilter = AllFilter;
        }

        var duts = _allRuns
            .Select(r => string.IsNullOrWhiteSpace(r.DutSerial) ? NoneDutFilter : r.DutSerial!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DutFilterOptions.Clear();
        DutFilterOptions.Add(AllFilter);
        foreach (var dut in duts)
        {
            DutFilterOptions.Add(dut);
        }

        if (!DutFilterOptions.Contains(DutFilter))
        {
            DutFilter = AllFilter;
        }
    }

    private void ApplyFilters()
    {
        var selectedId = SelectedRun?.RunId;
        Runs.Clear();
        foreach (var run in _allRuns.Where(MatchesFilters))
        {
            Runs.Add(run);
        }

        FilterStatus = _allRuns.Count == 0
            ? string.Empty
            : $"Showing {Runs.Count} of {_allRuns.Count}";

        if (selectedId is not null)
        {
            SelectedRun = Runs.FirstOrDefault(r =>
                string.Equals(r.RunId, selectedId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool MatchesFilters(TestRunSummary run)
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            var haystack = string.Join(
                ' ',
                run.PlanName,
                run.PlanId,
                run.DutSerial,
                run.DutPartNumber,
                run.OperatorName,
                run.RunId,
                run.SessionId);
            if (haystack.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        if (!string.Equals(ResultFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(ResultFilter, "Other", StringComparison.OrdinalIgnoreCase))
            {
                if (run.Result is RunResult.Passed or RunResult.Failed)
                {
                    return false;
                }
            }
            else if (!string.Equals(run.Result.ToString(), ResultFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.Equals(PlanFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            var planLabel = string.IsNullOrWhiteSpace(run.PlanName) ? run.PlanId : run.PlanName;
            if (!string.Equals(planLabel, PlanFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(run.PlanId, PlanFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.Equals(DutFilter, AllFilter, StringComparison.OrdinalIgnoreCase))
        {
            var dutLabel = string.IsNullOrWhiteSpace(run.DutSerial) ? NoneDutFilter : run.DutSerial!;
            if (!string.Equals(dutLabel, DutFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task OpenAsync()
    {
        if (SelectedRun is null)
        {
            Status = "Select a run first.";
            return;
        }

        OpenedRun = await _runStore.LoadAsync(SelectedRun.RunId);
        StepDetails.Clear();
        SampleDetails.Clear();
        PresentationTiles.Clear();
        HasPresentationTiles = false;
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

        foreach (var step in OpenedRun.Steps)
        {
            StepDetails.Add($"{step.StepId} [{step.StepType}] {(step.Passed ? "PASS" : "FAIL")} — {step.Message}");
        }

        foreach (var sample in OpenedRun.Samples.Take(200))
        {
            SampleDetails.Add(sample.ToDisplayLine());
        }

        foreach (var tile in PresentationRoleMap.BuildFromStoredSamples(OpenedRun.Samples))
        {
            PresentationTiles.Add(tile);
        }

        HasPresentationTiles = PresentationTiles.Count > 0;
        LoadReportItems(OpenedRun);

        Status = $"Opened {OpenedRun.RunId} ({OpenedRun.Result}) — {OpenedRun.Steps.Count} steps, {OpenedRun.Samples.Count} samples."
                 + (OpenedRun.SessionId is { } sid ? $" Session {sid[..Math.Min(8, sid.Length)]}." : string.Empty);
        if (!string.IsNullOrWhiteSpace(SchemaWarning))
        {
            Status += " " + SchemaWarning;
        }

        if (_dutHistory is not null)
        {
            var report = await _dutHistory.AnalyzeAsync(OpenedRun);
            HistorySummary = report.OperatorSummary;
            HistorySeverity = report.OverallSeverity.ToString();
            HasHistory = !string.IsNullOrWhiteSpace(report.OperatorSummary);
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

        // Detail pane only — default PDF is opened via double-click / OpenDefaultReportCommand.
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

    private void OpenReport(RunReportItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.PdfPath) || !File.Exists(item.PdfPath))
        {
            Status = "Report PDF not found.";
            return;
        }

        ReportOpened?.Invoke(this, item.PdfPath);
        Status = $"Opened {item.Title}.";
    }

    private async Task ReprintAsync()
    {
        if (SelectedRun is null)
        {
            Status = "Select a run first.";
            return;
        }

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
}
