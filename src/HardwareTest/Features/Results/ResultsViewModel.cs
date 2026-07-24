using System.Collections.ObjectModel;
using System.Threading.Tasks;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel : ReactiveObject
{
    private readonly IRunStore _runStore;
    private readonly IReportService _reportService;
    private readonly IDutHistoryService? _dutHistory;

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
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        ReprintCommand = ReactiveCommand.CreateFromTask(ReprintAsync);
        CloseDetailCommand = ReactiveCommand.Create(() =>
        {
            ShowDetail = false;
            HistorySummary = string.Empty;
            HistorySeverity = string.Empty;
        });
    }

    public ObservableCollection<TestRunSummary> Runs { get; }
    public ObservableCollection<string> StepDetails { get; }
    public ObservableCollection<string> SampleDetails { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ReprintCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CloseDetailCommand { get; }

    [Reactive] private TestRunSummary? _selectedRun;
    [Reactive] private TestRunRecord? _openedRun;
    [Reactive] private bool _showDetail;
    [Reactive] private string _status = "Loading runs…";
    [Reactive] private string _historySummary = string.Empty;
    [Reactive] private string _historySeverity = string.Empty;
    [Reactive] private bool _hasHistory;

    public event EventHandler<string>? ReportOpened;

    public async Task OpenSelectedRunAsync() => await OpenAsync();

    public Task LoadRunsAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        Runs.Clear();
        foreach (var run in await _runStore.ListAsync())
        {
            Runs.Add(run);
        }

        Status = Runs.Count == 0 ? "No runs yet." : $"Loaded {Runs.Count} run(s).";
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
        HistorySummary = string.Empty;
        HistorySeverity = string.Empty;
        HasHistory = false;
        if (OpenedRun is null)
        {
            ShowDetail = false;
            Status = "Run not found.";
            return;
        }

        ShowDetail = true;
        foreach (var step in OpenedRun.Steps)
        {
            StepDetails.Add($"{step.StepId} [{step.StepType}] {(step.Passed ? "PASS" : "FAIL")} — {step.Message}");
        }

        foreach (var sample in OpenedRun.Samples.Take(200))
        {
            SampleDetails.Add(sample.ToDisplayLine());
        }

        Status = $"Opened {OpenedRun.RunId} ({OpenedRun.Result}) — {OpenedRun.Steps.Count} steps, {OpenedRun.Samples.Count} samples."
                 + (OpenedRun.SessionId is { } sid ? $" Session {sid[..Math.Min(8, sid.Length)]}." : string.Empty);

        if (_dutHistory is not null)
        {
            var report = await _dutHistory.AnalyzeAsync(OpenedRun);
            HistorySummary = report.OperatorSummary;
            HistorySeverity = report.OverallSeverity.ToString();
            HasHistory = !string.IsNullOrWhiteSpace(report.OperatorSummary);
        }

        if (OpenedRun.ReportPdfPath is { } path && File.Exists(path))
        {
            ReportOpened?.Invoke(this, path);
        }
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
            var path = await _reportService.GeneratePdfAsync(run);
            OpenedRun = run;
            Status = $"Regenerated report: {path}";
            ReportOpened?.Invoke(this, path);
        }
        catch (Exception ex)
        {
            Status = $"Reprint failed: {ex.Message}";
        }
    }
}
