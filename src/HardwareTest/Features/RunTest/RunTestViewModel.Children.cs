using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    /// Builds child panels. Lambdas capture fields assigned later in this method — call only from the ctor.
    private void CreateChildPanels(
        IOpenTapPlanSession plan,
        IOpenTapRunSession runSession,
        IOpenTapStationSession station,
        OperatorSession session,
        IRunControl runControl,
        IReportService reportService,
        IRunStore runStore,
        AppSettings settings,
        ISettingsStore? settingsStore,
        IDutHistoryService? dutHistory,
        BuildInfo buildInfo,
        IStorageHealthService? storageHealth,
        IVisaModeController? visaModeController,
        ISafetyController? safety,
        IClock clock)
    {
        StepDetail = new StepDetailViewModel(() => OpenSelectedDetail(revealDetail: true));
        Interaction = new InteractionHostViewModel();
        Live = new LivePresentationViewModel();
        ProgramSelection = new ProgramSelectionViewModel(
            status => Status = status,
            () => IsEngineerDebugMode,
            () => LoadSelectedProgramAsync(),
            () => SessionPanel!.RefreshSessionSummary());
        SessionPanel = new OperatorSessionPanelViewModel(
            session,
            settings,
            status => Status = status,
            () => ProgramSelection.SelectedProgram,
            ClearSessionAttempts,
            clock);
        StationOverrides = new StationOverridesViewModel(
            plan,
            station,
            settings,
            settingsStore,
            status => Status = status,
            () => IsEngineerDebugMode,
            () => IsRunning,
            () => StepTree!.SelectedStep,
            () => ProgramSelection.SelectedProgram,
            summary => StepDetail.ConditionSummary = summary);
        StepTree = new StepTreeViewModel(
            () => _plan.StepTree,
            path => Run!.FindAttempt(path),
            () => OpenSelectedDetail(revealDetail: true),
            RefreshHero,
            () => CurrentStepPath);
        Run = new RunExecutionViewModel(
            this,
            runSession,
            station,
            session,
            runControl,
            reportService,
            runStore,
            settings,
            dutHistory,
            buildInfo,
            _progress,
            ProgramSelection,
            SessionPanel,
            StationOverrides,
            StepTree,
            StepDetail,
            Interaction,
            Live,
            storageHealth,
            visaModeController,
            safety,
            clock);
    }
}
