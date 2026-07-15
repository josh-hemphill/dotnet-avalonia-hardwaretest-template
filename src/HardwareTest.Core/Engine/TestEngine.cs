using System.Diagnostics;
using System.Globalization;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Runs;
using Serilog;
using Serilog.Context;

namespace HardwareTest.Core.Engine;

public interface ITestEngine
{
    Task<TestRunRecord> ExecuteAsync(
        TestPlan plan,
        IProgress<TestRunProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class TestEngine : ITestEngine
{
    private readonly IVisaSessionFactory _visaFactory;
    private readonly IRunStore _runStore;
    private readonly MeasurementAcquisition _acquisition;
    private readonly IRunControl _runControl;
    private readonly VisaSessionGate _gate;
    private readonly IAnalyzeAlgorithmResolver _algorithms;
    private readonly ILogger _logger;

    public TestEngine(
        IVisaSessionFactory visaFactory,
        IRunStore runStore,
        MeasurementAcquisition acquisition,
        IRunControl runControl,
        VisaSessionGate gate,
        IAnalyzeAlgorithmResolver algorithms,
        ILogger? logger = null)
    {
        _visaFactory = visaFactory;
        _runStore = runStore;
        _acquisition = acquisition;
        _runControl = runControl;
        _gate = gate;
        _algorithms = algorithms;
        _logger = logger ?? Log.ForContext<TestEngine>();
    }

    public async Task<TestRunRecord> ExecuteAsync(
        TestPlan plan,
        IProgress<TestRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        var record = new TestRunRecord
        {
            RunId = runId,
            PlanId = plan.Id,
            PlanName = plan.Name,
            DutSerial = plan.DutSerial,
            Resource = plan.Resource,
            StartedAt = DateTimeOffset.UtcNow,
            PlanSnapshot = plan,
        };

        using var runActivity = TestTracing.StartRun(runId, plan.Name, plan.DutSerial);
        record.TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

        using (LogContext.PushProperty("TestRunId", runId))
        using (LogContext.PushProperty("DutSerial", plan.DutSerial ?? string.Empty))
        {
            _logger.Information("Starting test run for plan {PlanName}", plan.Name);
            progress?.Report(new TestRunProgress { RunId = runId, Message = "Started" });

            IVisaSession? session = null;
            try
            {
                for (var i = 0; i < plan.Steps.Count; i++)
                {
                    await _runControl.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var step = plan.Steps[i];
                    var stepId = step.Id ?? $"step-{i + 1}";
                    var stepType = step.GetType().Name;
                    using var stepActivity = TestTracing.StartStep(stepId, stepType);
                    using (LogContext.PushProperty("StepId", stepId))
                    {
                        var stepStarted = DateTimeOffset.UtcNow;
                        progress?.Report(new TestRunProgress
                        {
                            RunId = runId,
                            StepId = stepId,
                            Message = $"Executing {stepType}",
                        });

                        var stepResult = await ExecuteStepAsync(
                            step,
                            stepId,
                            record,
                            () => session,
                            s => session = s,
                            progress,
                            cancellationToken).ConfigureAwait(false);

                        stepResult.StartedAt = stepStarted;
                        stepResult.CompletedAt = DateTimeOffset.UtcNow;
                        record.Steps.Add(stepResult);

                        if (!stepResult.Passed)
                        {
                            record.Result = RunResult.Failed;
                            record.ErrorMessage = stepResult.Message;
                            break;
                        }
                    }
                }

                if (record.Result == RunResult.Unknown)
                {
                    record.Result = RunResult.Passed;
                }
            }
            catch (OperationCanceledException)
            {
                record.Result = RunResult.Cancelled;
                record.ErrorMessage = _runControl.WasSafetyStopRequested ? "Safety stop" : "Cancelled";
                runActivity?.SetStatus(ActivityStatusCode.Error, record.ErrorMessage);
                _logger.Warning("Test run cancelled: {Reason}", record.ErrorMessage);
            }
            catch (Exception ex)
            {
                record.Result = RunResult.Error;
                record.ErrorMessage = ex.Message;
                runActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.Error(ex, "Test run failed");
            }
            finally
            {
                try
                {
                    await RunSafeShutdownAsync(plan, record, () => session, s => session = s, progress)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Safe shutdown failed for run {RunId}", runId);
                    record.ErrorMessage = string.IsNullOrWhiteSpace(record.ErrorMessage)
                        ? $"Safe shutdown failed: {ex.Message}"
                        : $"{record.ErrorMessage}; safe shutdown failed: {ex.Message}";
                }

                if (session is not null)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }

                record.CompletedAt = DateTimeOffset.UtcNow;
                record.Samples = _acquisition.Snapshot().Select(StoredSample.From).ToList();
                await _runStore.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new TestRunProgress
                {
                    RunId = runId,
                    Message = $"Completed: {record.Result}",
                    IsCompleted = true,
                    Result = record.Result,
                });
                _logger.Information("Test run {RunId} completed with {Result}", runId, record.Result);
            }
        }

        return record;
    }

    private async Task RunSafeShutdownAsync(
        TestPlan plan,
        TestRunRecord record,
        Func<IVisaSession?> getSession,
        Action<IVisaSession> setSession,
        IProgress<TestRunProgress>? progress)
    {
        var steps = plan.SafeShutdown;
        var session = getSession();
        if (steps.Count == 0 && session is null)
        {
            return;
        }

        if (steps.Count == 0)
        {
            return;
        }

        var token = _runControl.SafetyShutdownToken;
        progress?.Report(new TestRunProgress
        {
            RunId = record.RunId,
            Message = "Safe shutdown…",
        });

        using (_gate.BeginCriticalScope())
        {
            for (var i = 0; i < steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = steps[i];
                var stepId = step.Id ?? $"safe-{i + 1}";
                try
                {
                    if (getSession() is null && step is not OpenStep)
                    {
                        _logger.Warning("Skipping safe shutdown step {StepId}: no open session", stepId);
                        continue;
                    }

                    var stepResult = await ExecuteStepAsync(
                        step,
                        stepId,
                        record,
                        getSession,
                        setSession,
                        progress: null,
                        token).ConfigureAwait(false);
                    stepResult.StartedAt = DateTimeOffset.UtcNow;
                    stepResult.CompletedAt = DateTimeOffset.UtcNow;
                    record.Steps.Add(stepResult);
                    if (!stepResult.Passed)
                    {
                        _logger.Warning("Safe shutdown step {StepId} failed: {Message}", stepId, stepResult.Message);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Warning(ex, "Safe shutdown step {StepId} threw", stepId);
                    record.Steps.Add(new StepResultRecord
                    {
                        StepId = stepId,
                        StepType = step.GetType().Name,
                        Passed = false,
                        Message = ex.Message,
                        StartedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
        }
    }

    private async Task<StepResultRecord> ExecuteStepAsync(
        PlanStep step,
        string stepId,
        TestRunRecord record,
        Func<IVisaSession?> getSession,
        Action<IVisaSession> setSession,
        IProgress<TestRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case OpenStep open:
            {
                var resource = string.IsNullOrWhiteSpace(open.Resource)
                    ? record.Resource ?? "MOCK::INSTR0"
                    : open.Resource;
                var existing = getSession();
                if (existing is not null)
                {
                    await existing.DisposeAsync().ConfigureAwait(false);
                }

                var session = await _visaFactory.OpenAsync(resource, cancellationToken).ConfigureAwait(false);
                setSession(session);
                record.Resource = resource;
                return Pass(stepId, nameof(OpenStep), $"Opened {resource}");
            }
            case WriteStep write:
            {
                var session = RequireSession(getSession());
                await session.WriteAsync(write.Command, cancellationToken).ConfigureAwait(false);
                return Pass(stepId, nameof(WriteStep), write.Command);
            }
            case QueryStep query:
            {
                var session = RequireSession(getSession());
                var response = await session.QueryAsync(query.Command, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(query.StoreAs))
                {
                    record.Variables[query.StoreAs!] = response;
                }

                return Pass(stepId, nameof(QueryStep), response);
            }
            case DelayStep delay:
            {
                await Task.Delay(delay.Milliseconds, cancellationToken).ConfigureAwait(false);
                return Pass(stepId, nameof(DelayStep), $"{delay.Milliseconds} ms");
            }
            case AcquireStep acquire:
            {
                var session = RequireSession(getSession());
                await foreach (var sample in _acquisition.AcquireAsync(
                                   session,
                                   acquire.Channel,
                                   acquire.SampleCount,
                                   acquire.IntervalMs,
                                   acquire.QueryCommand,
                                   cancellationToken,
                                   _runControl).ConfigureAwait(false))
                {
                    progress?.Report(new TestRunProgress
                    {
                        RunId = record.RunId,
                        StepId = stepId,
                        Message = "Sample",
                        Sample = sample,
                    });
                }

                return Pass(stepId, nameof(AcquireStep), $"{acquire.SampleCount} samples on {acquire.Channel}");
            }
            case AssertStep assert:
            {
                var actual = ResolveAssertValue(assert.Source, record);
                var passed = Evaluate(assert.Operator, actual, assert.Value);
                var message = $"{assert.Source}={actual.ToString(CultureInfo.InvariantCulture)} {assert.Operator} {assert.Value.ToString(CultureInfo.InvariantCulture)}";
                return new StepResultRecord
                {
                    StepId = stepId,
                    StepType = nameof(AssertStep),
                    Passed = passed,
                    Message = message,
                };
            }
            case AnalyzeStep analyze:
            {
                var algo = _algorithms.Resolve(analyze.Algorithm);
                var result = algo.Execute(new AnalyzeContext
                {
                    Channel = analyze.Channel,
                    Threshold = analyze.Value,
                    Samples = _acquisition.Snapshot(),
                    Parameters = analyze.Parameters,
                    Record = record,
                });
                if (result.Metric is { } metric && !string.IsNullOrWhiteSpace(analyze.StoreAs))
                {
                    record.Variables[analyze.StoreAs!] = metric.ToString(CultureInfo.InvariantCulture);
                }

                return new StepResultRecord
                {
                    StepId = stepId,
                    StepType = nameof(AnalyzeStep),
                    Passed = result.Passed,
                    Message = result.Message,
                };
            }
            default:
                return new StepResultRecord
                {
                    StepId = stepId,
                    StepType = step.GetType().Name,
                    Passed = false,
                    Message = $"Unknown step type {step.GetType().Name}",
                };
        }
    }

    private static IVisaSession RequireSession(IVisaSession? session)
    {
        return session ?? throw new InvalidOperationException("No VISA session is open. Add an Open step first.");
    }

    private double ResolveAssertValue(string source, TestRunRecord record)
    {
        if (source.StartsWith("channel:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = source.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 && parts[2].Equals("mean", StringComparison.OrdinalIgnoreCase))
            {
                var channel = parts[1];
                var samples = _acquisition.Snapshot().Where(s => s.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (samples.Length == 0)
                {
                    return double.NaN;
                }

                return samples.Average(s => s.Value);
            }
        }

        if (record.Variables.TryGetValue(source, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unable to resolve assert source '{source}'.");
    }

    private static bool Evaluate(string op, double actual, double expected)
    {
        return op.ToLowerInvariant() switch
        {
            "eq" or "==" => Math.Abs(actual - expected) < 1e-9,
            "neq" or "!=" => Math.Abs(actual - expected) >= 1e-9,
            "gt" or ">" => actual > expected,
            "gte" or ">=" => actual >= expected,
            "lt" or "<" => actual < expected,
            "lte" or "<=" => actual <= expected,
            _ => throw new InvalidOperationException($"Unknown assert operator '{op}'."),
        };
    }

    private static StepResultRecord Pass(string stepId, string type, string message) => new()
    {
        StepId = stepId,
        StepType = type,
        Passed = true,
        Message = message,
    };
}
