using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Time;
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
    private readonly IExportTargetService? _exportTargets;
    private readonly OperatorSession? _operatorSession;
    private readonly IClock _clock;
    private readonly List<TestRunSummary> _allRuns = [];
    private readonly SemaphoreSlim _busyGate = new(1, 1);
    private bool _suppressAutoOpen;

    public ResultsViewModel(
        IRunStore runStore,
        IReportService reportService,
        IDutHistoryService? dutHistory = null,
        IExportTargetService? exportTargets = null,
        OperatorSession? operatorSession = null,
        IClock? clock = null)
    {
        _runStore = runStore;
        _reportService = reportService;
        _dutHistory = dutHistory;
        _exportTargets = exportTargets;
        _operatorSession = operatorSession;
        _clock = clock ?? SystemClock.Instance;
        Runs = [];
        StepDetails = [];
        SampleDetails = [];
        PresentationTiles = [];
        HistoryMetrics = [];
        ReportItems = [];
        ExportTargets = [];
        ResultFilterOptions = [AllFilter, nameof(RunResult.Passed), nameof(RunResult.Failed), "Other"];
        PlanFilterOptions = [AllFilter];
        DutFilterOptions = [AllFilter];
        ResultFilter = AllFilter;
        PlanFilter = AllFilter;
        DutFilter = AllFilter;
        OperatorFilter = AllFilter;
        DateFilter = DateAll;
        ClearFailedStepsFilterCommand = ReactiveCommand.Create(() =>
        {
            ShowFailedStepsOnly = false;
        });
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        OpenDefaultReportCommand = ReactiveCommand.CreateFromTask(OpenDefaultReportAsync);
        ReprintCommand = ReactiveCommand.CreateFromTask(ReprintAsync);
        OpenReportCommand = ReactiveCommand.Create<RunReportItemViewModel?>(OpenReport);
        ExportPackageCommand = ReactiveCommand.Create(ExportPackage);
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
            ShowFailedStepsOnly = false;
            FirstFailSummary = string.Empty;
            HasFirstFail = false;
            TriageSummary = string.Empty;
        });
        NavigateToRunCommand = ReactiveCommand.Create(
            () => NavigateToRunRequested?.Invoke(this, EventArgs.Empty));

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchText) or nameof(ResultFilter)
                or nameof(PlanFilter) or nameof(DutFilter)
                or nameof(OperatorFilter) or nameof(DateFilter))
            {
                ApplyFilters();
            }
            else if (args.PropertyName == nameof(ShowFailedStepsOnly) && OpenedRun is not null)
            {
                RebuildStepDetails();
            }
            else if (args.PropertyName == nameof(SelectedRun) && SelectedRun is not null && !_suppressAutoOpen)
            {
                ScheduleOpenDetail();
            }
        };

        RefreshExportTargets();
    }
}
