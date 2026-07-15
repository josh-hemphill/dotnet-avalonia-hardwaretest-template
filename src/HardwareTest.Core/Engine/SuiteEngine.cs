using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest.Core.Engine;

public interface ISuiteEngine
{
    Task<SuiteRunRecord> ExecuteAsync(
        TestSuite suite,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<SuiteRunRecord> ExecutePlanAsync(
        TestSuite suite,
        string planId,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class SuiteEngine : ISuiteEngine
{
    private readonly ITestEngine _testEngine;
    private readonly ISuiteRunStore _suiteRunStore;
    private readonly AppSettings _settings;
    private readonly ILogger _logger;

    public SuiteEngine(
        ITestEngine testEngine,
        ISuiteRunStore suiteRunStore,
        AppSettings settings,
        ILogger? logger = null)
    {
        _testEngine = testEngine;
        _suiteRunStore = suiteRunStore;
        _settings = settings;
        _logger = logger ?? Log.ForContext<SuiteEngine>();
    }

    public Task<SuiteRunRecord> ExecuteAsync(
        TestSuite suite,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(suite, planFilterId: null, progress, cancellationToken);

    public Task<SuiteRunRecord> ExecutePlanAsync(
        TestSuite suite,
        string planId,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(suite, planFilterId: planId, progress, cancellationToken);

    private async Task<SuiteRunRecord> ExecuteCoreAsync(
        TestSuite suite,
        string? planFilterId,
        IProgress<SuiteRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var suiteRunId = Guid.NewGuid().ToString("N");
        var record = new SuiteRunRecord
        {
            SuiteRunId = suiteRunId,
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            StartedAt = DateTimeOffset.UtcNow,
            SuiteSnapshot = suite,
        };

        var plans = string.IsNullOrWhiteSpace(planFilterId)
            ? suite.Plans.ToList()
            : suite.Plans.Where(p => string.Equals(p.Id, planFilterId, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(p.Name, planFilterId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (plans.Count == 0)
        {
            record.Result = RunResult.Error;
            record.ErrorMessage = $"No plans matched filter '{planFilterId}'.";
            record.CompletedAt = DateTimeOffset.UtcNow;
            await _suiteRunStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            return record;
        }

        progress?.Report(new SuiteRunProgress
        {
            SuiteRunId = suiteRunId,
            Message = $"Starting suite '{suite.Name}' ({plans.Count} plan(s))",
            PlanCount = plans.Count,
        });

        try
        {
            if (suite.ExecutionMode == SuiteExecutionMode.Parallel && string.IsNullOrWhiteSpace(planFilterId))
            {
                await RunParallelAsync(record, suite, plans, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunSequentialAsync(record, suite, plans, progress, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            record.Result = RunResult.Cancelled;
            record.ErrorMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            record.Result = RunResult.Error;
            record.ErrorMessage = ex.Message;
            _logger.Error(ex, "Suite {SuiteName} failed", suite.Name);
        }

        record.CompletedAt = DateTimeOffset.UtcNow;
        if (record.Result == RunResult.Unknown)
        {
            record.Result = AggregateResult(record.PlanRuns);
        }

        await _suiteRunStore.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
        progress?.Report(new SuiteRunProgress
        {
            SuiteRunId = suiteRunId,
            Message = $"Suite finished: {record.Result}",
            PlanCount = plans.Count,
            OverallPercent = 100,
            IsCompleted = true,
            Result = record.Result,
        });
        return record;
    }

    private async Task RunSequentialAsync(
        SuiteRunRecord record,
        TestSuite suite,
        List<TestPlan> plans,
        IProgress<SuiteRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < plans.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = PreparePlan(plans[i], suite);
            var planProgress = new Progress<TestRunProgress>(p =>
            {
                progress?.Report(new SuiteRunProgress
                {
                    SuiteRunId = record.SuiteRunId,
                    Message = p.Message,
                    PlanId = plan.Id,
                    PlanName = plan.Name,
                    PlanIndex = i,
                    PlanCount = plans.Count,
                    OverallPercent = (i + (p.IsCompleted ? 1.0 : 0.5)) / plans.Count * 100.0,
                    PlanProgress = p,
                });
            });

            progress?.Report(new SuiteRunProgress
            {
                SuiteRunId = record.SuiteRunId,
                Message = $"Running plan '{plan.Name}'",
                PlanId = plan.Id,
                PlanName = plan.Name,
                PlanIndex = i,
                PlanCount = plans.Count,
                OverallPercent = (double)i / plans.Count * 100.0,
            });

            var planRun = await _testEngine.ExecuteAsync(plan, planProgress, cancellationToken).ConfigureAwait(false);
            record.PlanRuns.Add(planRun);

            if (planRun.Result is RunResult.Failed or RunResult.Error or RunResult.Cancelled)
            {
                record.Result = planRun.Result;
                record.ErrorMessage = planRun.ErrorMessage;
                break;
            }
        }
    }

    private async Task RunParallelAsync(
        SuiteRunRecord record,
        TestSuite suite,
        List<TestPlan> plans,
        IProgress<SuiteRunProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tasks = plans.Select((plan, index) => Task.Run(async () =>
        {
            var prepared = PreparePlan(plan, suite);
            var planProgress = new Progress<TestRunProgress>(p =>
            {
                progress?.Report(new SuiteRunProgress
                {
                    SuiteRunId = record.SuiteRunId,
                    Message = p.Message,
                    PlanId = prepared.Id,
                    PlanName = prepared.Name,
                    PlanIndex = index,
                    PlanCount = plans.Count,
                    PlanProgress = p,
                });
            });
            return await _testEngine.ExecuteAsync(prepared, planProgress, cancellationToken).ConfigureAwait(false);
        }, cancellationToken)).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        record.PlanRuns.AddRange(results);
    }

    private TestPlan PreparePlan(TestPlan plan, TestSuite suite)
    {
        var roleMap = MergeRoleMaps(suite.Instruments, plan.Instruments);
        var safeShutdown = plan.SafeShutdown.Count > 0 ? plan.SafeShutdown : suite.SafeShutdown;

        return new TestPlan
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            DutSerial = plan.DutSerial,
            Resource = InstrumentResourceResolver.Resolve(plan.Resource, _settings, roleMap),
            Instruments = new Dictionary<string, string>(roleMap, StringComparer.OrdinalIgnoreCase),
            Steps = plan.Steps.Select(s => CloneStepWithResolvedOpen(s, roleMap)).ToList(),
            SafeShutdown = safeShutdown.Select(s => CloneStepWithResolvedOpen(s, roleMap)).ToList(),
        };
    }

    private static Dictionary<string, string> MergeRoleMaps(
        Dictionary<string, string> suiteMap,
        Dictionary<string, string> planMap)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in suiteMap)
        {
            merged[k] = v;
        }

        foreach (var (k, v) in planMap)
        {
            merged[k] = v;
        }

        return merged;
    }

    private PlanStep CloneStepWithResolvedOpen(PlanStep step, IReadOnlyDictionary<string, string> roleMap)
    {
        if (step is OpenStep open)
        {
            return new OpenStep
            {
                Id = open.Id,
                Resource = InstrumentResourceResolver.Resolve(
                    string.IsNullOrWhiteSpace(open.Resource) ? null : open.Resource,
                    _settings,
                    roleMap),
            };
        }

        return step;
    }

    private static RunResult AggregateResult(IReadOnlyList<TestRunRecord> runs)
    {
        if (runs.Count == 0)
        {
            return RunResult.Error;
        }

        if (runs.Any(r => r.Result == RunResult.Cancelled))
        {
            return RunResult.Cancelled;
        }

        if (runs.Any(r => r.Result == RunResult.Error))
        {
            return RunResult.Error;
        }

        if (runs.Any(r => r.Result == RunResult.Failed))
        {
            return RunResult.Failed;
        }

        return runs.All(r => r.Result == RunResult.Passed) ? RunResult.Passed : RunResult.Unknown;
    }
}
