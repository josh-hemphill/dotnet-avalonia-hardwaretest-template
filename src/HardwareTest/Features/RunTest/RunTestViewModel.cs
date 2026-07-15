using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

public partial class TestItemViewModel : ReactiveObject
{
    public TestItemViewModel(TestPlan plan)
    {
        Plan = plan;
        StatusText = "Pending";
        DetailLines = [];
    }

    public TestPlan Plan { get; }
    public ObservableCollection<string> DetailLines { get; }
    public string DisplayName => Plan.Name;

    [Reactive] private RunResult _result = RunResult.Unknown;
    [Reactive] private string _statusText = "Pending";
    [Reactive] private double _percent;
    [Reactive] private bool _hasPlot;
    [Reactive] private double[] _plotYs = Array.Empty<double>();
    [Reactive] private TestRunRecord? _lastRun;
}

public partial class SuiteQueueItemViewModel : ReactiveObject
{
    public SuiteQueueItemViewModel(TestSuite suite)
    {
        Suite = suite;
        StatusText = "Pending";
        Tests = new ObservableCollection<TestItemViewModel>(
            suite.Plans.Select(p => new TestItemViewModel(p)));
    }

    public TestSuite Suite { get; }
    public ObservableCollection<TestItemViewModel> Tests { get; }
    public string DisplayName => Suite.Name;
    public int PlanCount => Suite.Plans.Count;

    [Reactive] private RunResult _result = RunResult.Unknown;
    [Reactive] private string _statusText = "Pending";
    [Reactive] private double _percent;
}

public partial class RunTestViewModel : ReactiveObject
{
    private readonly ISuiteLoader _suiteLoader;
    private readonly ISuiteEngine _suiteEngine;
    private readonly IReportService _reportService;
    private readonly IRunControl _runControl;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;
    private readonly MeasurementAcquisition _liveBuffer = new(4096);
    private bool _stopAuto;
    private long _lastUiFlushTicks;
    private string? _pendingStatus;
    private double _pendingOverallPercent;
    private string? _pendingDetailLine;
    private bool _pendingPlot;

    /// Counts throttled plot UI flushes (tests).
    public int PlotUiFlushCount { get; private set; }

    public RunTestViewModel(
        ISuiteLoader suiteLoader,
        ISuiteEngine suiteEngine,
        IReportService reportService,
        AppSettings settings,
        IRunControl runControl)
    {
        _suiteLoader = suiteLoader;
        _suiteEngine = suiteEngine;
        _reportService = reportService;
        _settings = settings;
        _runControl = runControl;
        Status = "Add suites to the list, then Run (Auto advances).";
        SuiteQueue = [];
        EnabledInstruments = new ObservableCollection<VisaInstrument>(
            settings.Instruments.Where(i => i.Enabled));
        EmbeddedSuiteNames = new ObservableCollection<string>(_suiteLoader.ListEmbeddedSuiteNames());

        LoadSampleSuiteCommand = ReactiveCommand.CreateFromTask(LoadSampleSuiteAsync);
        OpenSuiteFileCommand = ReactiveCommand.CreateFromTask(OpenSuiteFileAsync);
        RemoveSelectedSuiteCommand = ReactiveCommand.Create(RemoveSelectedSuite);
        RunCommand = ReactiveCommand.CreateFromTask(RunAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ToggleDetailsCommand = ReactiveCommand.Create(() => { ShowDetails = !ShowDetails; });

        LoadPlanCommand = LoadSampleSuiteCommand;
        StartCommand = RunCommand;
        RunSuiteCommand = RunCommand;
        RunSelectedPlanCommand = RunCommand;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedSuiteItem))
            {
                OnSelectedSuiteItemChanged();
            }
            else if (e.PropertyName == nameof(SelectedTestItem))
            {
                OnSelectedTestItemChanged();
            }
            else if (e.PropertyName == nameof(ShowDetails))
            {
                RefreshPlotFromSelection();
            }
        };
    }

    public IRunControl RunControl => _runControl;

    public Func<CancellationToken, Task<string?>>? RequestSuiteFilePath { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadSampleSuiteCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenSuiteFileCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RemoveSelectedSuiteCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CancelCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleDetailsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadPlanCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> StartCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunSuiteCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunSelectedPlanCommand { get; }

    public ObservableCollection<SuiteQueueItemViewModel> SuiteQueue { get; }
    public ObservableCollection<VisaInstrument> EnabledInstruments { get; }
    public ObservableCollection<string> EmbeddedSuiteNames { get; }

    public ObservableCollection<TestItemViewModel> PlanItems =>
        SelectedSuiteItem?.Tests ?? _emptyTests;

    private static readonly ObservableCollection<TestItemViewModel> _emptyTests = [];

    [Reactive] private SuiteQueueItemViewModel? _selectedSuiteItem;
    [Reactive] private TestItemViewModel? _selectedTestItem;
    [Reactive] private VisaInstrument? _selectedInstrument;
    [Reactive] private string _status = string.Empty;
    [Reactive] private bool _isRunning;
    [Reactive] private string? _lastRunId;
    [Reactive] private double _overallPercent;
    [Reactive] private bool _isAutoMode = true;
    [Reactive] private bool _showDetails;
    [Reactive] private double[] _plotYs = Array.Empty<double>();

    public TestSuite? Suite => SelectedSuiteItem?.Suite;
    public TestPlan? Plan => SelectedTestItem?.Plan ?? SelectedSuiteItem?.Suite.Plans.FirstOrDefault();
    public ObservableCollection<string> Samples => SelectedTestItem?.DetailLines ?? [];

    public event EventHandler? PlotDataChanged;

    private void OnSelectedSuiteItemChanged()
    {
        SelectedTestItem = SelectedSuiteItem?.Tests.FirstOrDefault();
        this.RaisePropertyChanged(nameof(PlanItems));
        this.RaisePropertyChanged(nameof(Suite));
        this.RaisePropertyChanged(nameof(Plan));
        RefreshPlotFromSelection();
    }

    private void OnSelectedTestItemChanged()
    {
        this.RaisePropertyChanged(nameof(Plan));
        this.RaisePropertyChanged(nameof(Samples));
        RefreshPlotFromSelection();
    }

    private async Task LoadSampleSuiteAsync()
    {
        var suite = await _suiteLoader.LoadSampleSuiteAsync();
        EnqueueSuite(suite);
        Status = $"Added suite '{suite.Name}' ({suite.Plans.Count} tests).";
    }

    private async Task OpenSuiteFileAsync()
    {
        if (RequestSuiteFilePath is null)
        {
            Status = "File picker is unavailable in this host.";
            return;
        }

        var path = await RequestSuiteFilePath(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Open cancelled.";
            return;
        }

        var suite = await _suiteLoader.LoadSuiteFromFileAsync(path);
        EnqueueSuite(suite);
        Status = $"Added suite '{suite.Name}' from file.";
    }

    public void EnqueueSuite(TestSuite suite)
    {
        var item = new SuiteQueueItemViewModel(suite);
        SuiteQueue.Add(item);
        SelectedSuiteItem ??= item;
    }

    private void RemoveSelectedSuite()
    {
        if (SelectedSuiteItem is null || IsRunning)
        {
            Status = IsRunning ? "Cannot remove while running." : "Select a suite to remove.";
            return;
        }

        var idx = SuiteQueue.IndexOf(SelectedSuiteItem);
        SuiteQueue.Remove(SelectedSuiteItem);
        SelectedSuiteItem = SuiteQueue.Count == 0
            ? null
            : SuiteQueue[Math.Clamp(idx, 0, SuiteQueue.Count - 1)];
        Status = "Removed suite from list.";
    }

    private async Task RunAsync()
    {
        if (IsRunning)
        {
            Status = "Already running.";
            return;
        }

        if (SuiteQueue.Count == 0)
        {
            Status = "Load a suite first.";
            return;
        }

        _cts = new CancellationTokenSource();
        _stopAuto = false;
        PlotUiFlushCount = 0;
        _lastUiFlushTicks = 0;
        IsRunning = true;
        OverallPercent = 0;
        _runControl.AttachRun(_cts);

        try
        {
            var startIndex = SelectedSuiteItem is null
                ? 0
                : Math.Max(0, SuiteQueue.IndexOf(SelectedSuiteItem));

            if (startIndex < 0)
            {
                startIndex = 0;
            }

            for (var i = startIndex; i < SuiteQueue.Count; i++)
            {
                if (_cts.IsCancellationRequested || _stopAuto)
                {
                    Status = _runControl.WasSafetyStopRequested ? "Safety stop." : "Cancelled.";
                    break;
                }

                var queueItem = SuiteQueue[i];
                SelectedSuiteItem = queueItem;
                SelectedTestItem = queueItem.Tests.FirstOrDefault();
                ResetSuiteItem(queueItem);

                Status = IsAutoMode
                    ? $"Running suite '{queueItem.DisplayName}' (Auto)…"
                    : $"Running suite '{queueItem.DisplayName}'…";

                var suiteRun = await _suiteEngine.ExecuteAsync(queueItem.Suite, CreateProgress(queueItem), _cts.Token);
                FlushUi(force: true, queueItem, queueItem.Tests.FirstOrDefault());
                LastRunId = suiteRun.SuiteRunId;
                ApplySuiteResults(queueItem, suiteRun);

                if (suiteRun.Result is RunResult.Passed or RunResult.Failed)
                {
                    try
                    {
                        await _reportService.GenerateSuitePdfAsync(suiteRun);
                        Status = $"Finished '{queueItem.DisplayName}': {suiteRun.Result}. Report generated.";
                    }
                    catch (Exception ex)
                    {
                        Status = $"Finished '{queueItem.DisplayName}': {suiteRun.Result}. Report failed: {ex.Message}";
                    }
                }
                else
                {
                    Status = $"Finished '{queueItem.DisplayName}': {suiteRun.Result}";
                    if (_runControl.WasSafetyStopRequested)
                    {
                        Status = $"{Status} (safety stop)";
                    }
                }

                var shouldStop =
                    !IsAutoMode
                    || suiteRun.Result is RunResult.Failed or RunResult.Error or RunResult.Cancelled
                    || _cts.IsCancellationRequested
                    || _stopAuto;

                if (shouldStop)
                {
                    if (IsAutoMode && suiteRun.Result is RunResult.Failed or RunResult.Error)
                    {
                        Status = $"{Status} Auto stopped — select another suite or fix and Run again.";
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Status = _runControl.WasSafetyStopRequested ? "Safety stop." : "Cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            _runControl.DetachRun();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            OverallPercent = 100;
        }
    }

    private static void ResetSuiteItem(SuiteQueueItemViewModel queueItem)
    {
        queueItem.StatusText = "Running";
        queueItem.Result = RunResult.Unknown;
        queueItem.Percent = 0;
        foreach (var test in queueItem.Tests)
        {
            test.StatusText = "Queued";
            test.Result = RunResult.Unknown;
            test.Percent = 0;
            test.DetailLines.Clear();
            test.HasPlot = false;
            test.PlotYs = [];
            test.LastRun = null;
        }
    }

    private Progress<SuiteRunProgress> CreateProgress(SuiteQueueItemViewModel queueItem)
        => new(p => OnSuiteProgress(queueItem, p));

    private void OnSuiteProgress(SuiteQueueItemViewModel queueItem, SuiteRunProgress progress)
    {
        _pendingOverallPercent = progress.OverallPercent;
        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            _pendingStatus = progress.Message;
        }

        var item = queueItem.Tests.FirstOrDefault(p =>
            string.Equals(p.Plan.Id, progress.PlanId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Plan.Name, progress.PlanName, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            FlushUi(force: progress.IsCompleted, queueItem, null);
            return;
        }

        if (progress.PlanCount > 0)
        {
            item.Percent = ((progress.PlanIndex + 0.5) / progress.PlanCount) * 100.0;
        }

        if (progress.PlanProgress is { } planProgress)
        {
            if (planProgress.StepId is { } stepId)
            {
                _pendingDetailLine = $"{stepId}: {planProgress.Message}";
            }

            if (planProgress.Sample is { } sample)
            {
                // Always ingest samples (no data loss).
                _liveBuffer.Add(sample.Timestamp, sample.Value);
                item.HasPlot = true;
                item.PlotYs = _liveBuffer.Snapshot().Select(s => s.Value).ToArray();
                _pendingPlot = true;
                _pendingDetailLine =
                    $"{sample.Timestamp:HH:mm:ss.fff} {sample.Channel}={sample.Value:F4}";
            }

            if (planProgress.IsCompleted && planProgress.Result is { } result)
            {
                item.Result = result;
                item.StatusText = result.ToString();
                item.Percent = 100;
                FlushUi(force: true, queueItem, item);
                return;
            }
        }

        FlushUi(force: false, queueItem, item);
    }

    private void FlushUi(bool force, SuiteQueueItemViewModel queueItem, TestItemViewModel? item)
    {
        var hz = Math.Clamp(_settings.PlotRefreshHz, 1, 120);
        var intervalTicks = Stopwatch.Frequency / hz;
        var now = Stopwatch.GetTimestamp();
        if (!force && _lastUiFlushTicks != 0 && now - _lastUiFlushTicks < intervalTicks)
        {
            return;
        }

        _lastUiFlushTicks = now;
        OverallPercent = _pendingOverallPercent;
        queueItem.Percent = _pendingOverallPercent;
        if (_pendingStatus is not null)
        {
            queueItem.StatusText = _pendingStatus;
            Status = _runControl.IsPaused ? $"Paused — {_pendingStatus}" : _pendingStatus;
            if (item is not null && item.Result == RunResult.Unknown)
            {
                item.StatusText = _pendingStatus;
            }
        }

        if (item is not null && _pendingDetailLine is not null && ShowDetails)
        {
            AppendDetail(item, _pendingDetailLine);
        }

        _pendingDetailLine = null;

        if (_pendingPlot && item is not null)
        {
            if (ReferenceEquals(SelectedTestItem, item) && ShowDetails)
            {
                PlotYs = item.PlotYs;
                PlotDataChanged?.Invoke(this, EventArgs.Empty);
                PlotUiFlushCount++;
            }

            _pendingPlot = false;
        }
    }

    private static void AppendDetail(TestItemViewModel item, string line)
    {
        if (item.DetailLines.Count > 200)
        {
            item.DetailLines.RemoveAt(0);
        }

        item.DetailLines.Add(line);
    }

    private void ApplySuiteResults(SuiteQueueItemViewModel queueItem, SuiteRunRecord suiteRun)
    {
        queueItem.Result = suiteRun.Result;
        queueItem.StatusText = suiteRun.Result.ToString();
        queueItem.Percent = 100;

        foreach (var planRun in suiteRun.PlanRuns)
        {
            var item = queueItem.Tests.FirstOrDefault(p =>
                string.Equals(p.Plan.Id, planRun.PlanId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Plan.Name, planRun.PlanName, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                continue;
            }

            item.LastRun = planRun;
            item.Result = planRun.Result;
            item.StatusText = planRun.Result.ToString();
            item.Percent = 100;
            item.HasPlot = planRun.Samples.Count > 0;
            if (item.HasPlot)
            {
                item.PlotYs = planRun.Samples.Select(s => s.Value).ToArray();
            }
        }

        RefreshPlotFromSelection();
    }

    private void RefreshPlotFromSelection()
    {
        if (!ShowDetails || SelectedTestItem is null || !SelectedTestItem.HasPlot)
        {
            return;
        }

        PlotYs = SelectedTestItem.PlotYs;
        PlotDataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        _stopAuto = true;
        _runControl.RequestCancel();
        _cts?.Cancel();
    }
}
