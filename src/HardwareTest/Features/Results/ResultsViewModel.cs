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

    public ResultsViewModel(IRunStore runStore, IReportService reportService)
    {
        _runStore = runStore;
        _reportService = reportService;
        Runs = [];
        StepDetails = [];
        SampleDetails = [];
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        ReprintCommand = ReactiveCommand.CreateFromTask(ReprintAsync);
        CloseDetailCommand = ReactiveCommand.Create(() => { ShowDetail = false; });
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
    [Reactive] private string _status = "Click Refresh to load saved runs.";

    public event EventHandler<string>? ReportOpened;

    public async Task OpenSelectedRunAsync() => await OpenAsync();

    private async Task RefreshAsync()
    {
        Runs.Clear();
        foreach (var run in await _runStore.ListAsync())
        {
            Runs.Add(run);
        }

        Status = $"Loaded {Runs.Count} run(s).";
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
            SampleDetails.Add($"{sample.Channel}: {sample.Value:G6} @ {sample.Timestamp:u}");
        }

        Status = $"Opened {OpenedRun.RunId} ({OpenedRun.Result}) — {OpenedRun.Steps.Count} steps, {OpenedRun.Samples.Count} samples."
                 + (OpenedRun.SessionId is { } sid ? $" Session {sid[..Math.Min(8, sid.Length)]}." : string.Empty);

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
