using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

/// One execute: CTS, pause/interaction, samples, step results, and <see cref="IStepRuntime"/>.
/// Production <see cref="OpenTapSession"/> still single-flights; unit tests may construct two contexts.
public sealed class OpenTapRunContext : IStepRuntime, IDisposable
{
    private readonly OpenTapRunControlState _control = new();
    private readonly List<StoredSample> _samples = [];
    private readonly List<StepResultRecord> _steps = [];
    private readonly Dictionary<string, DateTimeOffset> _stepStarted = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly bool _cancelExecuteWithToken;
    private IProgress<OpenTapProgress>? _progress;

    public OpenTapRunContext(
        AppSettings settings,
        ILogger logger,
        bool cancelExecuteWithToken = false)
    {
        _settings = settings;
        _logger = logger;
        _cancelExecuteWithToken = cancelExecuteWithToken;
        _control.OperatorStateChanged += () => OperatorStateChanged?.Invoke();
    }

    public OpenTapRunControlState Control => _control;

    public IReadOnlyList<StoredSample> Samples => _samples;

    public IReadOnlyList<StepResultRecord> Steps => _steps;

    public bool IsAwaitingOperator => _control.IsAwaitingOperator;

    public string? OperatorPromptMessage => _control.OperatorPromptMessage;

    public OperatorInteractionRequest? PendingInteraction => _control.PendingInteraction;

    public event Action? OperatorStateChanged;

    public void Pause() => _control.Pause();

    public void Resume(OperatorInteractionResponse? response = null) => _control.Resume(response);

    public void Abort() => _control.Abort();

    public void WaitIfPaused() => _control.WaitIfPaused();

    public OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request)
    {
        _control.BeginPendingInteraction(request);
        _progress?.Report(new OpenTapProgress
        {
            Message = request.Message,
            AwaitingOperator = true,
            OperatorPromptMessage = request.Message,
            InteractionRequest = request,
            StatusText = "Awaiting operator",
        });
        return _control.WaitForInteractionResponse(request.Id);
    }

    public async Task<OpenTapRunSummary> ExecuteAsync(
        TestPlan plan,
        string? planDisplayName,
        DutIdentity? dutIdentity,
        HardwareDut? dut,
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        string runId,
        bool startPaused,
        Action<string, string?, string, string, string?> updateNode,
        Func<string, string?, string?> resolvePath,
        IReadOnlyList<StoredSample>? preservedSamples,
        IReadOnlyList<string>? sampleScopePaths)
    {
        _progress = progress;
        _samples.Clear();
        _steps.Clear();
        _stepStarted.Clear();
        _control.BeginRun(cancellationToken, startPaused: startPaused);

        var started = DateTimeOffset.UtcNow;
        runId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
        progress?.Report(new OpenTapProgress { Message = $"Starting '{planDisplayName ?? plan.Name}'", OverallPercent = 0 });

        try
        {
            var listener = new ProgressResultListener(
                progress,
                _samples,
                _steps,
                _stepStarted,
                updateNode,
                resolvePath,
                plan);
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
                                    _control.Token)
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
                        exportListener?.Close();
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            MergePreservedSamples(preservedSamples, sampleScopePaths);

            var verdict = planRun.Verdict;
            var result = MapVerdict(verdict, cancelled: _control.IsCancellationRequested);
            var summary = BuildSummary(
                runId,
                planDisplayName ?? plan.Name,
                dutIdentity,
                dut,
                result,
                started,
                verdict.ToString(),
                result == RunResult.Cancelled
                    ? (_control.IsCancellationRequested ? "Safety stop" : "Cancelled")
                    : (verdict == Verdict.Fail ? "One or more steps failed" : null));

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
            var summary = BuildSummary(
                runId,
                planDisplayName ?? plan.Name,
                dutIdentity,
                dut,
                RunResult.Cancelled,
                started,
                "Aborted",
                "Safety stop");
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
            _control.CompleteRun();
        }
    }

    public void Dispose() => _control.Dispose();

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
            CompletedAt = DateTimeOffset.UtcNow,
            Samples = _samples.ToList(),
            Steps = _steps.ToList(),
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
