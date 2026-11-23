using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

/// One execute: CTS, pause/interaction, samples, step results, and <see cref="IStepRuntime"/>.
/// Production <see cref="OpenTapSession"/> still single-flights; unit tests may construct two contexts.
public sealed class OpenTapRunContext : IStepRuntime, IOperatorReferenceAcceptanceRuntime, IDisposable
{
    private readonly List<StoredSample> _samples = [];
    private readonly List<StoredEvent> _events = [];
    private readonly List<StepResultRecord> _steps = [];
    private readonly Dictionary<string, DateTimeOffset> _stepStarted = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly bool _cancelExecuteWithToken;
    private readonly IClock _clock;
    private IProgress<OpenTapProgress>? _progress;

    public OpenTapRunContext(
        AppSettings settings,
        ILogger logger,
        bool cancelExecuteWithToken = false,
        IClock? clock = null,
        bool referenceAcceptancePrepared = false,
        bool referenceAcquisition = false)
    {
        _settings = settings;
        _logger = logger;
        _cancelExecuteWithToken = cancelExecuteWithToken;
        _clock = clock ?? SystemClock.Instance;
        IsOperatorReferenceAcceptancePrepared = referenceAcceptancePrepared;
        IsReferenceAcquisition = referenceAcceptancePrepared && referenceAcquisition;
        Control.OperatorStateChanged += () => OperatorStateChanged?.Invoke();
    }

    public OpenTapRunControlState Control { get; } = new();
    public bool IsOperatorReferenceAcceptancePrepared { get; }
    public bool IsReferenceAcquisition { get; }

    public IReadOnlyList<StoredSample> Samples => _samples;

    public IReadOnlyList<StoredEvent> Events => _events;

    public IReadOnlyList<StepResultRecord> Steps => _steps;

    public bool IsAwaitingOperator => Control.IsAwaitingOperator;

    public string? OperatorPromptMessage => Control.OperatorPromptMessage;

    public OperatorInteractionRequest? PendingInteraction => Control.PendingInteraction;

    public event Action? OperatorStateChanged;

    public void BeginRun(CancellationToken cancellationToken) => Control.BeginRun(cancellationToken);

    public void Pause() => Control.Pause();

    public void Resume(OperatorInteractionResponse? response = null) => Control.Resume(response);

    public void Abort() => Control.Abort();

    public void WaitIfPaused() => Control.WaitIfPaused();

    public OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request)
    {
        Control.BeginPendingInteraction(request);
        _progress?.Report(new OpenTapProgress
        {
            Message = request.Message,
            StepId = request.ProducerStepId?.ToString(),
            StepName = request.ProducerStepName,
            AwaitingOperator = true,
            OperatorPromptMessage = request.Message,
            InteractionRequest = request,
            StatusText = "Awaiting operator",
        });
        return Control.WaitForInteractionResponse(request.Id);
    }

    public async Task<OpenTapRunSummary> ExecuteAsync(
        TestPlan plan,
        string? planDisplayName,
        DutIdentity? dutIdentity,
        HardwareDut? dut,
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        string runId,
        Action<string, string?, string, string, string?> updateNode,
        Func<string, string?, string?> resolvePath,
        IReadOnlyList<StoredSample>? preservedSamples,
        IReadOnlyList<StoredEvent>? preservedEvents,
        IReadOnlyList<string>? sampleScopePaths)
    {
        _progress = progress;
        _samples.Clear();
        _events.Clear();
        _steps.Clear();
        _stepStarted.Clear();
        Control.BeginRun(cancellationToken);

        var started = _clock.UtcNow;
        runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
        progress?.Report(new OpenTapProgress { Message = $"Starting '{planDisplayName ?? plan.Name}'", OverallPercent = 0 });

        var deviceFailures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        string DeviceFailureText()
        {
            return deviceFailures.IsEmpty ? string.Empty
            : "Device I/O failed: " + string.Join("; ", deviceFailures) + ". Check Instruments bindings, timeout and device state before retrying.";
        }

        try
        {
            var listener = new ProgressResultListener(
                progress,
                _samples,
                _events,
                _steps,
                _stepStarted,
                updateNode,
                resolvePath,
                plan,
                _clock,
                () => Control.IsCancellationRequested);
            OpenTapFileResultExportListener? exportListener = null;
            if (_settings.ExportOpenTapResults)
            {
                if (string.IsNullOrWhiteSpace(_settings.DataDirectory))
                {
                    _logger.Warning(
                        "ExportOpenTapResults is enabled but DataDirectory is empty; skipping CSV export.");
                }
                else
                {
                    var exportDir = Path.Combine(
                        _settings.DataDirectory,
                        "runs",
                        SanitizeRunId(runId),
                        "opentap-results");
                    exportListener = new OpenTapFileResultExportListener(exportDir, _logger);
                }
            }

            var planRun = await Task.Run(
                async () =>
                {
                    var previousDeviceCancellation = BrokerDeviceConnector.RunCancellation;
                    var previousDeviceFailure = BrokerDeviceConnector.OnDeviceFailure;
                    var previousOperationFailure = global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.OperationFailure;
                    var previousExecutionGate = global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.WaitForExecution;
                    global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.WaitForExecution = WaitIfPaused;
                    global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.OperationFailure = (operation, error) =>
                    {
                        if (error.GetBaseException() is OperationCanceledException && Control.IsCancellationRequested) return;
                        deviceFailures.Enqueue(operation + ": " + error.Message);
                    };
                    BrokerDeviceConnector.OnDeviceFailure = deviceFailures.Enqueue;
                    BrokerDeviceConnector.RunCancellation = Control.Token;
                    StepRuntimeBinder.Attach(plan, this);
                    try
                    {
                        WaitIfPaused();
                        ResultListener[] listeners = exportListener is null
                            ? [listener]
                            : [listener, exportListener];
                        if (_cancelExecuteWithToken)
                        {
                            return await plan.ExecuteAsync(
                                    listeners,
                                    Array.Empty<ResultParameter>(),
                                    stepsOverride: null,
                                    Control.Token)
                                .ConfigureAwait(false);
                        }

                        // In-process test host: do not pass the run token into Execute.
                        // OpenTAP may map a cancelled token onto TapThread.Abort, which poisons
                        // later Execute calls in the same process (serial host suite).
                        return plan.Execute(listeners, []);
                    }
                    finally
                    {
                        StepRuntimeBinder.Detach(plan);
                        BrokerDeviceConnector.RunCancellation = previousDeviceCancellation;
                        BrokerDeviceConnector.OnDeviceFailure = previousDeviceFailure;
                        global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.OperationFailure = previousOperationFailure;
                        global::HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.WaitForExecution = previousExecutionGate;
                        exportListener?.Close();
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            var guidance = _events.LastOrDefault(mark => string.Equals(mark.Name, "OperatorGuidance", StringComparison.OrdinalIgnoreCase))?.Label;
            MergePreservedSamples(preservedSamples, sampleScopePaths);
            MergePreservedEvents(preservedEvents, sampleScopePaths);

            var verdict = planRun.Verdict;
            if (!deviceFailures.IsEmpty) verdict = Verdict.Error;
            var result = MapVerdict(verdict, cancelled: Control.IsCancellationRequested);
            var summary = BuildSummary(
                runId,
                planDisplayName ?? plan.Name,
                dutIdentity,
                dut,
                result,
                started,
                verdict.ToString(),
                result == RunResult.Cancelled
                    ? (Control.IsCancellationRequested ? "Safety stop" : "Cancelled") + (deviceFailures.IsEmpty ? "" : ". " + DeviceFailureText())
                    : (!deviceFailures.IsEmpty ? DeviceFailureText()
                        : verdict == Verdict.Fail ? "One or more steps failed" : verdict == Verdict.Error ? guidance : null));

            progress?.Report(new OpenTapProgress
            {
                Message = $"Completed: {summary.Result}",
                OverallPercent = 100,
                IsCompleted = true,
                Result = summary.Result,
            });
            return summary;
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex.GetType().Name.Contains("Abort", StringComparison.Ordinal))
        {
            MergePreservedSamples(preservedSamples, sampleScopePaths);
            MergePreservedEvents(preservedEvents, sampleScopePaths);
            var summary = BuildSummary(
                runId,
                planDisplayName ?? plan.Name,
                dutIdentity,
                dut,
                RunResult.Cancelled,
                started,
                "Aborted",
                "Safety stop" + (deviceFailures.IsEmpty ? "" : ". " + DeviceFailureText()));
            progress?.Report(new OpenTapProgress
            {
                Message = "Cancelled",
                OverallPercent = 100,
                IsCompleted = true,
                Result = RunResult.Cancelled,
            });
            return summary;
        }
        finally
        {
            _progress = null;
            Control.CompleteRun();
        }
    }

    public void Dispose() => Control.Dispose();

    private void MergePreservedSamples(
        IReadOnlyList<StoredSample>? preservedSamples,
        IReadOnlyList<string>? sampleScopePaths)
    {
        if (preservedSamples is null || preservedSamples.Count == 0 || sampleScopePaths is null)
        {
            return;
        }

        var prior = preservedSamples
            .Where(s => !OpenTapStepTree.IsPathUnderAnyScope(s.StepPath, sampleScopePaths))
            .ToList();
        var runSamples = _samples.ToList();
        var producedChannels = runSamples
            .Select(s => s.Channel)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Prior samples already exclude the selection scope by path. Drop path-less prior
        // samples when this run produced the same channel so new samples win.
        var kept = prior
            .Where(s => !(producedChannels.Contains(s.Channel) && string.IsNullOrWhiteSpace(s.StepPath)))
            .ToList();

        _samples.Clear();
        _samples.AddRange(kept);
        _samples.AddRange(runSamples);
    }

    private void MergePreservedEvents(
        IReadOnlyList<StoredEvent>? preservedEvents,
        IReadOnlyList<string>? sampleScopePaths)
    {
        if (preservedEvents is null || preservedEvents.Count == 0 || sampleScopePaths is null)
        {
            return;
        }

        var prior = preservedEvents
            .Where(e => !OpenTapStepTree.IsPathUnderAnyScope(e.StepPath, sampleScopePaths))
            .ToList();
        var runEvents = _events.ToList();
        _events.Clear();
        _events.AddRange(prior);
        _events.AddRange(runEvents);
    }

    private OpenTapRunSummary BuildSummary(
        string runId,
        string planName,
        DutIdentity? dutIdentity,
        HardwareDut? dut,
        RunResult result,
        DateTimeOffset started,
        string verdict,
        string? error)
        => new()
        {
            RunId = runId,
            PlanName = planName,
            Result = result,
            ErrorMessage = error,
            DutSerial = dutIdentity?.Serial ?? dut?.SerialNumber,
            DutPartNumber = dutIdentity?.PartNumber ?? dut?.PartNumber,
            DutRevision = dutIdentity?.Revision ?? dut?.Revision,
            StartedAt = started,
            CompletedAt = _clock.UtcNow,
            Samples = [.. _samples],
            Events = [.. _events],
            Steps = [.. _steps],
            Verdict = verdict,
        };

    private static string SanitizeRunId(string runId)
        => PortableFileNames.Sanitize(runId);

    private static RunResult MapVerdict(Verdict verdict, bool cancelled)
    {
        if (cancelled)
        {
            return RunResult.Cancelled;
        }

        return verdict switch
        {
            Verdict.Pass => RunResult.Passed,
            Verdict.Fail => RunResult.Failed,
            Verdict.Error => RunResult.Error,
            Verdict.Aborted => RunResult.Cancelled,
            _ => RunResult.Unknown,
        };
    }
}
