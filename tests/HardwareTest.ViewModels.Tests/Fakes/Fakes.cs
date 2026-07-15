using System.ComponentModel;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;

namespace HardwareTest.ViewModels.Tests.Fakes;

public sealed class FakePlanLoader : IPlanLoader, ISuiteLoader
{
    public TestPlan Plan { get; set; } = new()
    {
        Id = "fake",
        Name = "Fake Plan",
        Resource = "MOCK::0",
        Steps = [new DelayStep { Milliseconds = 1 }],
    };

    public TestSuite Suite { get; set; } = new()
    {
        Id = "fake-suite",
        Name = "Fake Suite",
        Plans =
        [
            new TestPlan
            {
                Id = "fake",
                Name = "Fake Plan",
                Resource = "MOCK::0",
                Steps = [new DelayStep { Milliseconds = 1 }],
            },
        ],
    };

    public Task<TestPlan> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(Plan);

    public Task<TestPlan> LoadSampleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Plan);

    public IReadOnlyList<string> ListEmbeddedPlanNames() => ["fake.json"];

    public Task<TestSuite> LoadSuiteFromFileAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(Suite);

    public Task<TestSuite> LoadSampleSuiteAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Suite);

    public IReadOnlyList<string> ListEmbeddedSuiteNames() => ["fake-suite.json"];
}

public sealed class FakeTestEngine : ITestEngine
{
    public TestRunRecord Result { get; set; } = new()
    {
        RunId = "run1",
        PlanName = "Fake Plan",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Result = RunResult.Passed,
    };

    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(30);
    public bool ReportProgressSamples { get; set; } = true;
    public int ExecuteCount { get; private set; }

    public async Task<TestRunRecord> ExecuteAsync(
        TestPlan plan,
        IProgress<TestRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExecuteCount++;
        progress?.Report(new TestRunProgress { RunId = Result.RunId, Message = "Started" });
        if (ReportProgressSamples)
        {
            progress?.Report(new TestRunProgress
            {
                RunId = Result.RunId,
                Message = "Sample",
                Sample = new MeasurementSample("VDC", DateTimeOffset.UtcNow, 1.25),
            });
        }

        try
        {
            await Task.Delay(Delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Result = new TestRunRecord
            {
                RunId = Result.RunId,
                PlanName = plan.Name,
                StartedAt = Result.StartedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Cancelled,
            };
            progress?.Report(new TestRunProgress
            {
                RunId = Result.RunId,
                Message = $"Completed: {Result.Result}",
                IsCompleted = true,
                Result = Result.Result,
            });
            return Result;
        }

        progress?.Report(new TestRunProgress
        {
            RunId = Result.RunId,
            Message = $"Completed: {Result.Result}",
            IsCompleted = true,
            Result = Result.Result,
        });
        return Result;
    }
}

public sealed class FakeSuiteEngine : ISuiteEngine
{
    public SuiteRunRecord Result { get; set; } = new()
    {
        SuiteRunId = "suite1",
        SuiteName = "Fake Suite",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Result = RunResult.Passed,
        PlanRuns =
        [
            new TestRunRecord
            {
                RunId = "run1",
                PlanId = "fake",
                PlanName = "Fake Plan",
                StartedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Passed,
                Samples = [new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.25 }],
            },
        ],
    };

    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(30);
    public int ExecuteCount { get; private set; }
    public RunResult CompletionResult { get; set; } = RunResult.Passed;

    public async Task<SuiteRunRecord> ExecuteAsync(
        TestSuite suite,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExecuteCount++;
        progress?.Report(new SuiteRunProgress
        {
            SuiteRunId = Result.SuiteRunId,
            Message = "Started",
            PlanCount = suite.Plans.Count,
        });

        var plan = suite.Plans.FirstOrDefault();
        if (plan is not null)
        {
            progress?.Report(new SuiteRunProgress
            {
                SuiteRunId = Result.SuiteRunId,
                Message = "Sample",
                PlanId = plan.Id,
                PlanName = plan.Name,
                PlanIndex = 0,
                PlanCount = suite.Plans.Count,
                PlanProgress = new TestRunProgress
                {
                    RunId = "run1",
                    Message = "Sample",
                    Sample = new MeasurementSample("VDC", DateTimeOffset.UtcNow, 1.25),
                },
            });
        }

        try
        {
            await Task.Delay(Delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Result = new SuiteRunRecord
            {
                SuiteRunId = Result.SuiteRunId,
                SuiteName = suite.Name,
                StartedAt = Result.StartedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = RunResult.Cancelled,
            };
            progress?.Report(new SuiteRunProgress
            {
                SuiteRunId = Result.SuiteRunId,
                Message = "Cancelled",
                IsCompleted = true,
                Result = RunResult.Cancelled,
            });
            return Result;
        }

        var planResult = CompletionResult == RunResult.Passed ? RunResult.Passed : CompletionResult;
        Result = new SuiteRunRecord
        {
            SuiteRunId = Result.SuiteRunId,
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            StartedAt = Result.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Result = CompletionResult,
            PlanRuns = Result.PlanRuns.Count > 0
                ? Result.PlanRuns.Select(p =>
                {
                    p.Result = planResult;
                    return p;
                }).ToList()
                :
                [
                    new TestRunRecord
                    {
                        RunId = "run1",
                        PlanId = plan?.Id ?? "fake",
                        PlanName = plan?.Name ?? "Fake Plan",
                        StartedAt = DateTimeOffset.UtcNow,
                        Result = planResult,
                        Samples = [new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.25 }],
                    },
                ],
        };

        progress?.Report(new SuiteRunProgress
        {
            SuiteRunId = Result.SuiteRunId,
            Message = $"Completed: {Result.Result}",
            PlanCount = suite.Plans.Count,
            OverallPercent = 100,
            IsCompleted = true,
            Result = Result.Result,
        });
        return Result;
    }

    public async Task<SuiteRunRecord> ExecutePlanAsync(
        TestSuite suite,
        string planId,
        IProgress<SuiteRunProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(suite, progress, cancellationToken);
}

public sealed class FakeRunControl : IRunControl
{
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private CancellationTokenSource? _runCts;

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsSafetyStopping { get; private set; }
    public bool WasSafetyStopRequested { get; private set; }
    public CancellationToken SafetyShutdownToken => CancellationToken.None;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachRun(CancellationTokenSource runCts)
    {
        _runCts = runCts;
        IsRunning = true;
        IsPaused = false;
        IsSafetyStopping = false;
        WasSafetyStopRequested = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
    }

    public void DetachRun()
    {
        _runCts = null;
        IsRunning = false;
        IsPaused = false;
        IsSafetyStopping = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        IsPaused = true;
        _pauseEvent.Reset();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void Resume()
    {
        IsPaused = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void RequestCancel()
    {
        Resume();
        _runCts?.Cancel();
    }

    public void RequestSafetyStop()
    {
        WasSafetyStopRequested = true;
        IsSafetyStopping = true;
        Resume();
        _runCts?.Cancel();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSafetyStopping)));
    }

    public void CancelSafetyShutdown()
    {
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        while (!_pauseEvent.IsSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pauseEvent.Wait(50);
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeReportService : IReportService
{
    public int GenerateCount { get; private set; }
    public string PdfPath { get; set; } = Path.GetTempFileName();

    public Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        GenerateCount++;
        run.ReportPdfPath = PdfPath;
        return Task.FromResult(PdfPath);
    }

    public Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
        GenerateCount++;
        suiteRun.ReportPdfPath = PdfPath;
        return Task.FromResult(PdfPath);
    }

    public Task<byte[]> CompileTemplateAsync(TestRunRecord run, CancellationToken cancellationToken = default)
        => Task.FromResult("%PDF-fake"u8.ToArray());
}

public sealed class FakeRunStore : IRunStore
{
    private readonly Dictionary<string, TestRunRecord> _runs = new(StringComparer.Ordinal);

    public void Seed(TestRunRecord run) => _runs[run.RunId] = run;

    public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        _runs[run.RunId] = run;
        return Task.CompletedTask;
    }

    public Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
        => Task.FromResult(_runs.TryGetValue(runId, out var r) ? r : null);

    public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TestRunSummary> list = _runs.Values
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new TestRunSummary
            {
                RunId = r.RunId,
                PlanName = r.PlanName,
                StartedAt = r.StartedAt,
                Result = r.Result,
                DutSerial = r.DutSerial,
            })
            .ToArray();
        return Task.FromResult(list);
    }

    public string GetRunDirectory(string runId)
        => Path.Combine(Path.GetTempPath(), "fake-runs", runId);
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public FakeSettingsStore()
    {
        AppSettings = new AppSettings { UseMockVisa = true, DefaultVisaResource = "MOCK::0" };
        UiState = new UiState { SelectedPageId = "Home" };
        RootDirectory = Path.Combine(Path.GetTempPath(), "fake-settings");
        RunsDirectory = Path.Combine(RootDirectory, "runs");
    }

    public AppSettings AppSettings { get; }
    public UiState UiState { get; }
    public string RootDirectory { get; }
    public string RunsDirectory { get; }
    public int SaveAppCount { get; private set; }
    public int SaveUiCount { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        SaveAppCount++;
        return Task.CompletedTask;
    }

    public Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        SaveUiCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeVisaDiscovery : IVisaResourceDiscovery
{
    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(MockVisaResourceDiscovery.Catalog);
}
