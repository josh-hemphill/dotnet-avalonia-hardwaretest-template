using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using Serilog.Context;
using StepFilter = HardwareTest.Features.RunTest.StepStatusFilter;

namespace HardwareTest.Features.RunTest;

/// Run / Run Selected / Cancel pipeline: station binding, execution, attempt ledger, persistence and reports.
public sealed class RunExecutionViewModel
{
    private readonly IRunBoardHost _host;
    private readonly IOpenTapSession _openTap;
    private readonly OperatorSession _session;
    private readonly IRunControl _runControl;
    private readonly IReportService _reportService;
    private readonly IRunStore _runStore;
    private readonly AppSettings _settings;
    private readonly IDutHistoryService? _dutHistory;
    private readonly BuildInfo _buildInfo;
    private readonly IProgress<OpenTapProgress> _progress;
    private readonly ProgramSelectionViewModel _programs;
    private readonly OperatorSessionPanelViewModel _sessionPanel;
    private readonly StationOverridesViewModel _stationOverrides;
    private readonly StepTreeViewModel _stepTree;
    private readonly StepDetailViewModel _stepDetail;
    private readonly InteractionHostViewModel _interaction;
    private readonly LivePresentationViewModel _live;
    private readonly IStorageHealthService? _storageHealth;

    private readonly Dictionary<string, StepAttemptSummary> _attemptLedger =
        new(StringComparer.OrdinalIgnoreCase);

    public RunExecutionViewModel(
        IRunBoardHost host,
        IOpenTapSession openTap,
        OperatorSession session,
        IRunControl runControl,
        IReportService reportService,
        IRunStore runStore,
        AppSettings settings,
        IDutHistoryService? dutHistory,
        BuildInfo buildInfo,
        IProgress<OpenTapProgress> progress,
        ProgramSelectionViewModel programs,
        OperatorSessionPanelViewModel sessionPanel,
        StationOverridesViewModel stationOverrides,
        StepTreeViewModel stepTree,
        StepDetailViewModel stepDetail,
        InteractionHostViewModel interaction,
        LivePresentationViewModel live,
        IStorageHealthService? storageHealth = null)
    {
        _host = host;
        _openTap = openTap;
        _session = session;
        _runControl = runControl;
        _reportService = reportService;
        _runStore = runStore;
        _settings = settings;
        _dutHistory = dutHistory;
        _buildInfo = buildInfo;
        _progress = progress;
        _programs = programs;
        _sessionPanel = sessionPanel;
        _stationOverrides = stationOverrides;
        _stepTree = stepTree;
        _stepDetail = stepDetail;
        _interaction = interaction;
        _live = live;
        _storageHealth = storageHealth;

        RunCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: false));
        RunSelectedCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: true));
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RunSelectedCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CancelCommand { get; }

    public StepAttemptSummary? FindAttempt(string stepPath)
        => _attemptLedger.TryGetValue(stepPath, out var ledger) ? ledger : null;

    public void ClearAttempts() => _attemptLedger.Clear();

    public void Cancel()
    {
        _runControl.RequestSafetyStop();
        _openTap.Abort(safetyStop: true);
    }

    private async Task ExecuteRunAsync(bool selectionOnly)
    {
        if (_host.IsRunning)
        {
            _host.Status = "Already running.";
            return;
        }

        if (_host is RunTestViewModel runBoard)
        {
            runBoard.RefreshStorageHealth();
        }

        var health = _storageHealth?.GetDataVolumeHealth();
        if (health?.Level == StorageHealthLevel.Critical)
        {
            _host.Status = health.Message + " Clear space or adjust retention under Settings → Storage.";
            return;
        }

        _sessionPanel.ApplyIdleStaleCheck();
        if (!_session.CanRun)
        {
            _sessionPanel.ShowSessionForm = true;
            _host.Status = _session.State == OperatorSessionState.Stale
                ? $"Still testing {_session.DutSerial}? Confirm Same DUT or Change Session."
                : "Confirm DUT to run.";
            _sessionPanel.RefreshSessionSummary();
            return;
        }

        var program = _programs.SelectedProgram;
        if (program is null)
        {
            _host.Status = "Select a program.";
            return;
        }

        var selectionPath = selectionOnly ? _stepTree.SelectedStep?.Path : null;
        var selectionName = selectionOnly ? _stepTree.SelectedStep?.Name : null;
        var stagePath = _stepTree.SelectedStage?.Path;
        if (selectionOnly)
        {
            if (string.IsNullOrWhiteSpace(selectionPath))
            {
                _host.Status = "Select a stage or step to run.";
                return;
            }

            if (_stepTree.IsWholePlanSelection(selectionPath))
            {
                _host.Status = "Run Selected needs a specific stage or step — not the entire program. Use Run for the full suite.";
                return;
            }
        }

        _host.IsRunning = true;
        _live.ResetForRun();
        _host.HistoryBanner = string.Empty;
        _host.ResetPumpForRun();
        _stepDetail.DetailLines.Clear();
        _interaction.Clear();
        _host.OverallPercent = 0;
        _host.HasBanner = false;
        _host.BannerMessage = string.Empty;
        _host.IterationText = string.Empty;
        var cts = new CancellationTokenSource();
        _runControl.AttachRun(cts);

        try
        {
            await RunPipelineAsync(selectionOnly, selectionPath, selectionName, stagePath, program, cts.Token);
        }
        catch (Exception ex)
        {
            _host.Status = $"Error: {ex.Message}";
            _host.SetBanner(RunBannerSeverity.Error, $"Run error: {ex.Message}");
        }
        finally
        {
            _runControl.DetachRun();
            _host.IsRunning = false;
            _interaction.IsAwaitingOperator = false;
            _interaction.OperatorPromptMessage = null;
            _interaction.Clear();
            _host.OverallPercent = 0;
            cts.Dispose();
            _sessionPanel.RefreshSessionSummary();
            _host.RefreshHero();
        }
    }

    private async Task RunPipelineAsync(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        string? stagePath,
        ProgramItemViewModel program,
        CancellationToken cancellationToken)
    {
        await _host.LoadSelectedProgramAsync(stagePath, selectionPath);
        _stationOverrides.ApplySavedParameterOverrides();
        if (_host.IsEngineerDebugMode && !string.IsNullOrWhiteSpace(selectionPath))
        {
            _stepTree.SelectedStep = _stepTree.FindByPath(selectionPath) ?? _stepTree.SelectedStep;
            if (_stepTree.SelectedStep is not null)
            {
                _stationOverrides.ApplyDebugPatch();
            }
        }

        var station = _stationOverrides.BuildStationProfile();
        var unbound = _openTap.InstrumentSlots
            .Where(s => string.IsNullOrWhiteSpace(s.ResourceName)
                        && !station.RoleToResource.ContainsKey(s.RoleHint)
                        && !station.RoleToResource.ContainsKey(s.Name))
            .Select(s => s.Name)
            .ToList();
        if (unbound.Count > 0)
        {
            var msg = $"Bind unbound instrument slots on Instruments page: {string.Join(", ", unbound)}";
            _host.Status = msg;
            _host.SetBanner(RunBannerSeverity.Error, msg);
            _host.OverallPercent = 0;
            return;
        }

        if (!_settings.UseMockVisa)
        {
            var mockSlots = _openTap.InstrumentSlots
                .Select(s =>
                {
                    if (station.RoleToResource.TryGetValue(s.RoleHint, out var byRole)
                        && !string.IsNullOrWhiteSpace(byRole))
                    {
                        return (Slot: s, Resource: byRole.Trim());
                    }

                    if (station.RoleToResource.TryGetValue(s.Name, out var byName)
                        && !string.IsNullOrWhiteSpace(byName))
                    {
                        return (Slot: s, Resource: byName.Trim());
                    }

                    return (Slot: s, Resource: s.ResourceName?.Trim() ?? string.Empty);
                })
                .Where(x => MockResourceGuard.LooksLikeMockResource(x.Resource)
                            || MockResourceGuard.IsMockInstrumentType(x.Slot.TypeName))
                .Select(x => x.Slot.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (mockSlots.Count > 0)
            {
                var msg =
                    $"Mock instruments/resources blocked while Use mock VISA is off. Bind real addresses on Instruments for: {string.Join(", ", mockSlots)}";
                _host.Status = msg;
                _host.SetBanner(RunBannerSeverity.Error, msg);
                _host.OverallPercent = 0;
                return;
            }
        }

        await _openTap.ApplyStationAndDutAsync(station, _session.ToDutIdentity());
        _session.TouchActivity();

        var runId = Guid.NewGuid().ToString("N");
        _host.LastRunId = runId;
        await _runStore.SaveAsync(new TestRunRecord
        {
            RunId = runId,
            PlanId = program.Id,
            PlanName = program.DisplayName,
            DutSerial = _session.DutSerial,
            DutPartNumber = _session.DutPartNumber,
            DutRevision = _session.DutRevision,
            SessionId = _session.SessionId,
            OperatorName = _session.OperatorName,
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Unknown,
            AppVersion = _buildInfo.InformationalVersion,
            AppCommitSha = _buildInfo.CommitSha,
        }).ConfigureAwait(false);

        using var runLog = LogContext.PushProperty("TestRunId", runId);
        var summary = selectionOnly
            ? await _openTap.RunSelectionAsync(
                selectionPath!,
                _progress,
                cancellationToken,
                runId,
                includeCleanup: program.SelectionIncludesCleanup).ConfigureAwait(false)
            : await _openTap.RunAsync(_progress, cancellationToken, runId).ConfigureAwait(false);

        _host.ForceUiFlush();
        await _host.WaitForPendingFlushesAsync().ConfigureAwait(false);

        _host.LastRunId = summary.RunId;
        RecordAttempts(summary);

        var terminal = summary.Result is RunResult.Passed or RunResult.Failed
            or RunResult.Error or RunResult.Cancelled;
        await _host.RunOnUiAsync(() =>
        {
            PublishRunOutcome(selectionOnly, selectionPath, selectionName, summary);
            if (terminal && _settings.RequireDutConfirmEveryRun)
            {
                _sessionPanel.ApplyConfirmEveryRunPolicy(runReachedTerminal: true);
            }
            else if (terminal)
            {
                _session.TouchActivity();
                _sessionPanel.RefreshSessionSummary();
            }
        }).ConfigureAwait(false);

        var record = new TestRunRecord
        {
            RunId = summary.RunId,
            PlanId = program.Id,
            PlanName = summary.PlanName,
            DutSerial = summary.DutSerial ?? _session.DutSerial,
            DutPartNumber = summary.DutPartNumber ?? _session.DutPartNumber,
            DutRevision = summary.DutRevision ?? _session.DutRevision,
            SessionId = _session.SessionId,
            OperatorName = _session.OperatorName,
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            Result = summary.Result,
            ErrorMessage = summary.ErrorMessage,
            Samples = summary.Samples,
            Steps = BuildRolledUpSteps(),
            StepAttempts = _attemptLedger.Values.OrderBy(a => a.StepPath).ToList(),
            AppVersion = _buildInfo.InformationalVersion,
            AppCommitSha = _buildInfo.CommitSha,
        };
        await _runStore.SaveAsync(record).ConfigureAwait(false);

        var historyReport = await AnalyzeHistoryAsync(record, summary.Result).ConfigureAwait(false);
        await GenerateReportsAsync(record, program, summary.Result, historyReport).ConfigureAwait(false);
    }

    private void PublishRunOutcome(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        _host.SyncHierarchyLive();
        _stepTree.ApplyStepResults(summary.Steps);
        _stepTree.ApplyAttemptTexts();
        if (!selectionOnly && summary.Result == RunResult.Failed)
        {
            _stepTree.StepStatusFilter = StepFilter.Fail;
        }

        if (!string.IsNullOrWhiteSpace(selectionPath))
        {
            _stepTree.ResolveSelectedStep(selectionPath);
        }

        _host.OpenSelectedDetail(revealDetail: false);
        _host.Status = BuildCompletionStatus(selectionOnly, selectionPath, selectionName, summary);
        _stepDetail.AttemptSummaryChip = string.Empty;
        if (selectionOnly
            && !string.IsNullOrWhiteSpace(selectionPath)
            && _attemptLedger.TryGetValue(selectionPath, out var ledger))
        {
            _stepDetail.AttemptSummaryChip = ledger.Display;
        }
    }

    private async Task<DutHistoryReport?> AnalyzeHistoryAsync(TestRunRecord record, RunResult result)
    {
        if (_dutHistory is null || result is not (RunResult.Passed or RunResult.Failed))
        {
            return null;
        }

        try
        {
            var report = await _dutHistory.AnalyzeAsync(record).ConfigureAwait(false);
            await _host.RunOnUiAsync(() =>
            {
                if (!_settings.ShowDutHistoryOnRun)
                {
                    return;
                }

                _host.HistoryBanner = report.OperatorSummary;
                if (!string.IsNullOrWhiteSpace(report.OperatorSummary))
                {
                    _host.Status += " " + report.OperatorSummary;
                }
            }).ConfigureAwait(false);
            return report;
        }
        catch (Exception ex)
        {
            if (_settings.ShowDutHistoryOnRun)
            {
                await _host.RunOnUiAsync(() => _host.HistoryBanner = $"DUT history unavailable: {ex.Message}")
                    .ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task GenerateReportsAsync(
        TestRunRecord record,
        ProgramItemViewModel program,
        RunResult result,
        DutHistoryReport? historyReport)
    {
        if (result is not (RunResult.Passed or RunResult.Failed))
        {
            return;
        }

        try
        {
            var kinds = program.ReportKinds is { Count: > 0 }
                ? program.ReportKinds
                : ProgramCatalog.ResolveReportKinds(program.Id);
            await _reportService.GenerateReportsAsync(record, kinds, historyReport).ConfigureAwait(false);
            await _host.RunOnUiAsync(() => _host.Status += " Report(s) generated.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _host.RunOnUiAsync(() => _host.Status += $" Report failed: {ex.Message}").ConfigureAwait(false);
        }
    }

    private string BuildCompletionStatus(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        if (selectionOnly && !string.IsNullOrWhiteSpace(selectionPath))
        {
            _attemptLedger.TryGetValue(selectionPath, out var ledger);
            var attemptNo = ledger?.AttemptCount ?? 1;
            var badge = ledger?.Display ?? $"{attemptNo}";
            return $"Attempt #{attemptNo} for {selectionName ?? selectionPath} ({badge}). Snapshot also saved to Results.";
        }

        return $"Suite finished: {summary.Result}. Session attempts rolled up; Results entry {summary.RunId}.";
    }

    private void RecordAttempts(OpenTapRunSummary summary)
    {
        foreach (var step in summary.Steps)
        {
            var path = string.IsNullOrWhiteSpace(step.StepPath) ? step.StepType : step.StepPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!_attemptLedger.TryGetValue(path, out var ledger))
            {
                ledger = new StepAttemptSummary
                {
                    StepPath = path,
                    StepName = step.StepType,
                };
                _attemptLedger[path] = ledger;
            }

            ledger.Attempts.Add(new StepResultRecord
            {
                StepId = step.StepId,
                StepType = step.StepType,
                StepPath = path,
                AttemptNumber = ledger.AttemptCount + 1,
                Passed = step.Passed,
                Message = step.Message,
                StartedAt = step.StartedAt,
                CompletedAt = step.CompletedAt,
            });
            ledger.AttemptCount++;
            if (step.Passed)
            {
                ledger.PassedCount++;
            }
            else
            {
                ledger.FailedCount++;
            }

            ledger.LatestPassed = step.Passed;
            ledger.LatestMessage = step.Message;
            ledger.StepName = step.StepType;
        }
    }

    private List<StepResultRecord> BuildRolledUpSteps()
        => _attemptLedger.Values
            .Select(l => l.Attempts.LastOrDefault())
            .Where(a => a is not null)
            .Cast<StepResultRecord>()
            .OrderBy(a => a.StepPath)
            .ToList();
}
