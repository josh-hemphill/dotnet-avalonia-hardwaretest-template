using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.StationHealth;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Text;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using Serilog.Context;

namespace HardwareTest.Features.RunTest;

/// Run / Run Selected / Cancel pipeline: station binding, execution, attempt ledger, persistence and reports.
public sealed partial class RunExecutionViewModel
{
    private readonly IRunBoardHost _host;
    private readonly IOpenTapRunSession _runSession;
    private readonly IOpenTapStationSession _station;
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
    private readonly IVisaModeController? _visaModeController;
    private readonly ISafetyController? _safety;
    private readonly IClock _clock;
    private readonly Action<string, IReadOnlyList<string>>? _onStationNotReady;
    private readonly IStationHealthStore? _stationHealthStore;
    private readonly IStationHealthGate? _stationHealthGate;

    private readonly Dictionary<string, StepAttemptSummary> _attemptLedger =
        new(StringComparer.OrdinalIgnoreCase);

    public RunExecutionViewModel(
        IRunBoardHost host,
        IOpenTapRunSession runSession,
        IOpenTapStationSession station,
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
        IStorageHealthService? storageHealth = null,
        IVisaModeController? visaModeController = null,
        ISafetyController? safety = null,
        IClock? clock = null,
        Action<string, IReadOnlyList<string>>? onStationNotReady = null,
        IStationHealthStore? stationHealthStore = null,
        IStationHealthGate? stationHealthGate = null)
    {
        _host = host;
        _runSession = runSession;
        _station = station;
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
        _visaModeController = visaModeController;
        _safety = safety;
        _clock = clock ?? SystemClock.Instance;
        _onStationNotReady = onStationNotReady;
        _stationHealthStore = stationHealthStore;
        _stationHealthGate = stationHealthGate;

        RunCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: false));
        RunSelectedCommand = ReactiveCommand.CreateFromTask(() => ExecuteRunAsync(selectionOnly: true));
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RunCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RunSelectedCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CancelCommand { get; }

    public StepAttemptSummary? FindAttempt(string stepPath)
        => _attemptLedger.TryGetValue(stepPath, out var ledger) ? ledger : null;

    public void ClearAttempts() => _attemptLedger.Clear();
    public void Cancel()
    {
        if (_runControl.IsSafetyStopping)
        {
            _runControl.CancelSafetyShutdown();
            return;
        }

        if (!_runControl.IsRunning && !_runSession.IsAwaitingOperator)
        {
            return;
        }

        if (_runControl.IsRunning)
        {
            _runControl.RequestSafetyStop();
            try
            {
                _safety?.SafeIdle();
            }
            catch
            {
                // continue abort even if the adapter throws
            }
        }

        _runSession.Abort(safetyStop: true);
    }

    private async Task ExecuteRunAsync(bool selectionOnly)
    {
        if (_host.IsRunning)
        {
            BlockStart(RunBannerSeverity.Warning, "Already running.");
            return;
        }
        if (_host is RunTestViewModel managingBoard && managingBoard.References.IsManagementPending)
        {
            BlockStart(RunBannerSeverity.Warning, "Reference management is still in progress. Wait for its saved result and confirm the reference context before Run.");
            return;
        }

        if (_host is RunTestViewModel runBoard)
        {
            runBoard.RefreshStorageHealth();
        }

        var health = _storageHealth?.GetDataVolumeHealth();
        if (health?.Level == StorageHealthLevel.Critical)
        {
            BlockStart(
                RunBannerSeverity.Error,
                health.Message + " Clear space or adjust retention under Settings → Storage.");
            return;
        }

        _sessionPanel.ApplyIdleStaleCheck();
        _sessionPanel.RefreshRequirementFlags();
        _sessionPanel.RefreshSessionSummary();
        if (_sessionPanel.SessionBlocked || !_session.CanRun)
        {
            _sessionPanel.ShowSessionForm = true;
            var msg = _sessionPanel.IsStalePrompt || _sessionPanel.IsIdleWarningPrompt
                ? $"Still testing {_session.DutSerial}? Confirm Same DUT or Change Session."
                : "Confirm DUT and required session information before Run.";
            _host.Status = msg;
            if (_host is RunTestViewModel sessionBoard) sessionBoard.SetSessionGateBanner(RunBannerSeverity.Warning, msg);
            else _host.SetBanner(RunBannerSeverity.Warning, msg);
            _sessionPanel.RefreshSessionSummary();
            return;
        }

        var program = _programs.SelectedProgram;
        if (program is null)
        {
            BlockStart(RunBannerSeverity.Warning, "Select a program.");
            return;
        }

        if (_host is RunTestViewModel selectedBoard && !selectedBoard.IsProgramReadyFor(program))
        {
            _host.Status = selectedBoard.ProgramAvailabilityMessage;
            return;
        }

        if (_stationHealthGate is not null)
        {
            var gate = _stationHealthGate.Evaluate(program.ToGateRequest(), _clock);
            if (string.Equals(gate.Level, StationHealthGateLevels.Block, StringComparison.OrdinalIgnoreCase))
            {
                BlockStart(RunBannerSeverity.Error, gate.Message);
                return;
            }

            if (string.Equals(gate.Level, StationHealthGateLevels.Warn, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(gate.Message))
            {
                _host.SetBanner(RunBannerSeverity.Warning, gate.Message);
            }
        }

        var selectionPath = selectionOnly ? _stepTree.SelectedStep?.Path : null;
        var selectionName = selectionOnly ? _stepTree.SelectedStep?.Name : null;
        var stagePath = _stepTree.SelectedStage?.Path;
        if (selectionOnly)
        {
            if (string.IsNullOrWhiteSpace(selectionPath))
            {
                BlockStart(RunBannerSeverity.Warning, "Select a visible stage or step to run.");
                return;
            }

            if (_stepTree.IsReadOnlyEvidenceSelection)
            {
                BlockStart(RunBannerSeverity.Warning, "Reference acceptance is read-only evidence. Select the measured test or use Run to evaluate it again.");
                return;
            }

            if (_stepTree.IsWholePlanSelection(selectionPath))
            {
                BlockStart(
                    RunBannerSeverity.Warning,
                    "Run Selected needs a specific stage or step — not the entire program. Use Run for the full suite.");
                return;
            }

            if (!_stepTree.IsSelectedStepVisible())
            {
                BlockStart(RunBannerSeverity.Warning, "Select a visible stage or step to run.");
                return;
            }
        }

        _host.IsRunning = true;
        if (_host is RunTestViewModel resettingBoard) resettingBoard.SetReferenceOutcome(null, [], RunResult.Unknown);
        if (_host is RunTestViewModel startingBoard) startingBoard.BeginRunNotifications();
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
            await _host.RunOnUiAsync(() =>
            {
                _host.Status = $"Error: {ex.Message}";
                _host.SetBanner(RunBannerSeverity.Error, $"Run error: {ex.Message}");
            }).ConfigureAwait(false);
        }
        finally
        {
            await _host.RunOnUiAsync(() =>
            {
                _runControl.DetachRun();
                _host.IsRunning = false;
                _interaction.IsAwaitingOperator = false;
                _interaction.OperatorPromptMessage = null;
                _interaction.Clear();
                _host.OverallPercent = 0;
                _sessionPanel.RefreshSessionSummary();
                _host.RefreshHero();
            }).ConfigureAwait(false);
            cts.Dispose();
        }
    }

    private void BlockStart(RunBannerSeverity severity, string message)
    {
        _host.Status = message;
        _host.SetBanner(severity, message);
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
        if (_host is RunTestViewModel loadedBoard && !loadedBoard.IsProgramReadyFor(program))
        {
            await _host.RunOnUiAsync(() => _host.Status = loadedBoard.ProgramAvailabilityMessage)
                .ConfigureAwait(false);
            return;
        }
        _stationOverrides.ApplySavedParameterOverrides();
        var station = _stationOverrides.BuildStationProfile();
        if (_host.IsEngineerDebugMode && !string.IsNullOrWhiteSpace(selectionPath) && program.ReferenceRules.Count == 0)
        {
            _stepTree.SelectedStep = _stepTree.FindByPath(selectionPath) ?? _stepTree.SelectedStep;
            if (_stepTree.SelectedStep is not null && _stationOverrides.TryApplyDebugPatchForRunPreparation())
            {
                var resources = new Dictionary<string, string>(station.SlotToResource, StringComparer.OrdinalIgnoreCase)
                {
                    [_stationOverrides.DebugSlotName!] = _stationOverrides.DebugResource.Trim(),
                };
                station = new StationProfile(resources);
            }
        }

        var snapshots = _station.InstrumentSlots.Select(s =>
        {
            var resource = s.ResourceName?.Trim() ?? string.Empty;
            if (station.SlotToResource.TryGetValue(s.Name, out var byName)
                && !string.IsNullOrWhiteSpace(byName))
            {
                resource = byName.Trim();
            }

            return new StationSlotSnapshot
            {
                SlotName = s.Name,
                PlanId = program.Id,
                RoleHint = s.RoleHint,
                TypeName = s.TypeName,
                EffectiveResource = resource,
            };
        }).ToList();
        var readiness = StationReadinessEvaluator.Evaluate(
            snapshots,
            _visaModeController?.EffectiveUseMockVisa ?? _settings.UseMockVisa);
        if (!readiness.CanRun)
        {
            _host.Status = readiness.OperatorSummary;
            _host.SetBanner(RunBannerSeverity.Error, readiness.OperatorSummary);
            _host.OverallPercent = 0;
            _onStationNotReady?.Invoke(program.Id, readiness.BlockingSlotNames);
            return;
        }

        var references = _host is RunTestViewModel referenceBoard
            ? await referenceBoard.FreezeBenchReferencesAsync(program, selectionOnly).ConfigureAwait(false) : null;
        if (references is not null)
            station = station with { ReferenceAcceptancePrepared = true, ReferenceAcquisition = references.IsAcquisition };
        await _station.ApplyStationAndDutAsync(station, _session.ToDutIdentity());
        _session.TouchActivity();

        var runId = Guid.NewGuid().ToString("N");
        _host.LastRunId = runId;
        await _runStore.SaveAsync(new TestRunRecord
        {
            RunId = runId,
            PlanId = program.Id,
            PlanName = program.DisplayName,
            ExecutionScope = references?.IsAcquisition == true ? RunExecutionScopes.ReferenceAcquisition
                : selectionOnly ? RunExecutionScopes.SelectedStep : RunExecutionScopes.FullSuite,
            SelectedStepPath = selectionOnly ? selectionPath : null,
            ReferenceContext = references?.Context,
            ReferenceRules = references?.Rules.Select(BenchReferenceEvaluator.CloneRule).ToList() ?? [],
            ReferenceUses = references?.Uses.Select(BenchReferenceEvaluator.CloneUse).ToList() ?? [],
            DutSerial = _session.DutSerial,
            DutPartNumber = _session.DutPartNumber,
            DutRevision = _session.DutRevision,
            SessionId = _session.SessionId,
            OperatorName = _session.OperatorName,
            StartedAt = _clock.UtcNow,
            Result = RunResult.Unknown,
            AppVersion = _buildInfo.InformationalVersion,
            AppCommitSha = _buildInfo.CommitSha,
        }).ConfigureAwait(false);

        using var runLog = LogContext.PushProperty("TestRunId", runId);
        await _host.RunOnUiAsync(() =>
        {
            if (_host is RunTestViewModel savedBoard) savedBoard.MarkLastRunSaved(runId);
        }).ConfigureAwait(false);
        var summary = selectionOnly
            ? await _runSession.RunSelectionAsync(
                selectionPath!,
                _progress,
                cancellationToken,
                runId,
                includeCleanup: program.SelectionIncludesCleanup).ConfigureAwait(false)
            : await _runSession.RunAsync(_progress, cancellationToken, runId).ConfigureAwait(false);

        _host.ForceUiFlush();
        await _host.WaitForPendingFlushesAsync().ConfigureAwait(false);

        var backendResult = summary.Result;
        var referenceRecord = new TestRunRecord
        {
            ReferenceRules = references?.Rules.Select(BenchReferenceEvaluator.CloneRule).ToList() ?? [],
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            SelectedStepPath = selectionOnly ? selectionPath : null,
            ExecutionScope = references?.IsAcquisition == true ? RunExecutionScopes.ReferenceAcquisition
                : selectionOnly ? RunExecutionScopes.SelectedStep : RunExecutionScopes.FullSuite,
            Samples = summary.Samples,
        };
        var referenceEvaluations = references is { IsAcquisition: false } && summary.Result is RunResult.Passed or RunResult.Failed
            ? BenchReferenceEvaluator.Evaluate(referenceRecord, references.Uses).ToList() : [];
        if (referenceEvaluations.Count > 0) summary = WithReferenceOutcome(summary, referenceEvaluations);
        _host.LastRunId = summary.RunId;
        RecordAttempts(summary);

        var terminal = summary.Result is RunResult.Passed or RunResult.Failed
            or RunResult.Error or RunResult.Cancelled;
        await _host.RunOnUiAsync(() =>
        {
            PublishRunOutcome(selectionOnly, selectionPath, selectionName, summary);
            if (_host is RunTestViewModel outcomeBoard)
                outcomeBoard.SetReferenceOutcome(references, referenceEvaluations, backendResult);
            if (references?.IsAcquisition == true)
            {
                _host.Status = $"Reference acquisition: {backendResult}. Not a production pass. Save qualified values from Results.";
                if (_host is RunTestViewModel acquisitionBoard) acquisitionBoard.SetRunCompletionBanner(RunBannerSeverity.Warning, _host.Status);
            }
            else if (referenceEvaluations.Any(evaluation => !evaluation.Passed))
            {
                _host.Status = $"Reference acceptance: Failed. Saved result {ShortId.Display(summary.RunId)}.";
                if (_host is RunTestViewModel failedBoard) failedBoard.SetRunCompletionBanner(RunBannerSeverity.Error, _host.Status);
            }
            if (ProgramKinds.IsStationHealth(program.ProgramKind) && (references?.IsAcquisition == true || selectionOnly))
            {
                _host.Status += " Health qualification was not updated; complete a full production run.";
                var severity = summary.Result is RunResult.Failed or RunResult.Error ? RunBannerSeverity.Error : RunBannerSeverity.Warning;
                if (_host is RunTestViewModel healthBoard) healthBoard.SetRunCompletionBanner(severity, _host.Status);
            }
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
            ExecutionScope = references?.IsAcquisition == true ? RunExecutionScopes.ReferenceAcquisition
                : selectionOnly ? RunExecutionScopes.SelectedStep : RunExecutionScopes.FullSuite,
            SelectedStepPath = selectionOnly ? selectionPath : null,
            ReferenceContext = references?.Context,
            ReferenceRules = references?.Rules.Select(BenchReferenceEvaluator.CloneRule).ToList() ?? [],
            ReferenceUses = references?.Uses.Select(BenchReferenceEvaluator.CloneUse).ToList() ?? [],
            ReferenceEvaluations = referenceEvaluations,
            ReferenceBackendResult = references is null ? null : backendResult,
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
            Events = summary.Events,
            Steps = BuildRolledUpSteps(),
            StepAttempts = _attemptLedger.Values.OrderBy(a => a.StepPath).ToList(),
            AppVersion = _buildInfo.InformationalVersion,
            AppCommitSha = _buildInfo.CommitSha,
        };
        await _runStore.SaveAsync(record).ConfigureAwait(false);
        await _host.RunOnUiAsync(() =>
        {
            if (_host is RunTestViewModel savedBoard) savedBoard.MarkLastRunSaved(record.RunId);
        }).ConfigureAwait(false);
        if (_stationHealthStore is not null && references?.IsAcquisition != true && !selectionOnly)
        {
            await StationHealthRecorder.TryWriteAsync(
                    _stationHealthStore,
                    _clock,
                    program.ProgramKind,
                    program.StationHealthProfileId,
                    program.Id,
                    record.RunId,
                    record.Result,
                    record.Samples)
                .ConfigureAwait(false);
        }

        var historyReport = await AnalyzeHistoryAsync(record, summary.Result).ConfigureAwait(false);
        await GenerateReportsAsync(record, program, summary.Result, historyReport).ConfigureAwait(false);
    }

}
