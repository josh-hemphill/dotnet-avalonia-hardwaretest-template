using System;
using System.Threading.Tasks;

namespace HardwareTest.Features.RunTest;

/// Severity for sticky Run pipeline notices (mirrored to the shell strip; not in-page Auto chrome).
public enum RunBannerSeverity
{
    Info,
    Warning,
    Error,
}

/// UI flush pump used by the run pipeline (and stubs in child unit tests).
public interface IRunBoardUiPump
{
    /// Marshals onto the UI thread (or the injected test scheduler). Under a live app,
    /// a missing dispatcher logs and drops — the action is never run off-thread.
    Task RunOnUiAsync(Action action);

    /// Completes once queued progress has been drained onto the UI.
    Task WaitForPendingFlushesAsync();

    /// Marks the pump dirty and schedules a flush even if the throttle window has not elapsed.
    void ForceUiFlush();

    /// Drops queued progress and resets flush accounting before a new run starts.
    void ResetPumpForRun();
}

/// Coordinator surface the run pipeline needs: shared run status, UI flush pump, and cross-child refreshes.
/// Implemented by <see cref="RunTestViewModel"/>; a stub implementation makes the children unit-testable.
public interface IRunBoardHost : IRunBoardUiPump
{
    string Status { get; set; }
    bool IsRunning { get; set; }
    double OverallPercent { get; set; }
    string? LastRunId { get; set; }
    string HistoryBanner { get; set; }
    string IterationText { get; set; }
    bool IsEngineerDebugMode { get; }

    /// Whether a sticky severity notice is active (host state; presented by the shell strip).
    bool HasBanner { get; set; }
    /// Severity of the sticky notice.
    RunBannerSeverity BannerSeverity { get; set; }
    /// Message text of the sticky notice.
    string BannerMessage { get; set; }

    /// Sets sticky severity notice state and publishes it to the shell strip when wired.
    void SetBanner(RunBannerSeverity severity, string message);

    Task LoadSelectedProgramAsync(string? preserveStagePath = null, string? preserveStepPath = null);

    /// Re-mirrors host node state onto the tree and refreshes hero + shown detail.
    void SyncHierarchyLive();

    void OpenSelectedDetail(bool revealDetail);

    void RefreshHero();
}
