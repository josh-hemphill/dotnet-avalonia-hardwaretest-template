using System.ComponentModel;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host;

/// Plan load and step-tree surface — used by Inspect and the Run board program loader.
public interface IOpenTapPlanSession
{
    string? LoadedPlanPath { get; }
    string? LoadedPlanName { get; }
    IReadOnlyList<OpenTapStepNode> StepTree { get; }

    Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default);
    Task LoadSampleProgramAsync(CancellationToken cancellationToken = default);
    Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default);
    Task LoadSweepDemoProgramAsync(CancellationToken cancellationToken = default);

    bool TrySetStepEnabled(string stepPath, bool enabled);
    bool TryGetStepConditionSummary(string stepPath, out string? summary);
}

/// Run / pause / interaction surface — used by chrome Safety Stop and the Run execution pipeline.
/// Named to avoid collision with Core <c>IRunControl</c> (VISA/engine pause-cancel).
public interface IOpenTapRunSession : INotifyPropertyChanged
{
    /// True while <see cref="RunAsync"/> / <see cref="RunSelectionAsync"/> holds the single-flight gate.
    bool IsExecuting { get; }
    bool IsAwaitingOperator { get; }
    string? OperatorPromptMessage { get; }
    OperatorInteractionRequest? PendingInteraction { get; }

    Task<OpenTapRunSummary> RunAsync(
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null);
    Task<OpenTapRunSummary> RunSelectionAsync(
        string stepPath,
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null,
        bool includeCleanup = true);
    void Pause();
    void Resume(OperatorInteractionResponse? response = null);
    void Abort(bool safetyStop = false);
}

/// Station slots, DUT bind, and parameter/mixin bridge — used by Station Overrides and Run binding.
public interface IOpenTapStationSession
{
    IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots { get; }

    Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default);

    /// Sample adapter — prefer <see cref="TrySetParameter"/> / TypeData bridge for new code.
    bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs);
    /// Sample adapter — prefer <see cref="TrySetParameter"/> / TypeData bridge for new code.
    bool TrySetMeanGteThreshold(string stepPath, double threshold);
    bool TryRebindDmmResource(string resource);
    bool TryBindSlotResource(string slotName, string resource);

    IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides);
    bool TryGetParameter(string memberKey, out string? value);
    bool TrySetParameter(string memberKey, string value);
}

/// Packages, plugin dirs, and OpenTAP device discovery — used by Settings and Instruments.
public interface IOpenTapHostCatalog
{
    IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories();
    IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages();
    IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses();
}
