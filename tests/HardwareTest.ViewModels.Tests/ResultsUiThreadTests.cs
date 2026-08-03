using System.Collections.Specialized;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Yielding store so ConfigureAwait(false) continuations leave the caller thread.
internal sealed class YieldingRunStore : IRunStore
{
    private readonly List<TestRunRecord> _runs = [];

    public void Seed(TestRunRecord run) => _runs.Add(run);

    public string GetRunDirectory(string runId) => Path.Combine(Path.GetTempPath(), runId);

    public async Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _runs.Add(run);
    }

    public async Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _runs.FirstOrDefault(r => r.RunId == runId);
    }

    public async Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return _runs
            .Select(r => new TestRunSummary
            {
                RunId = r.RunId,
                PlanName = r.PlanName,
                PlanId = r.PlanId,
                StartedAt = r.StartedAt,
                Result = r.Result,
                DutSerial = r.DutSerial,
            })
            .OrderByDescending(r => r.StartedAt)
            .ToArray();
    }
}

public sealed class ResultsUiThreadTests
{
    [Fact]
    public async Task Open_mutates_StepDetails_only_via_UiScheduler()
    {
        var store = new YieldingRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "r1",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s",
                    StepType = "AcquireVoltageStep",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
            Samples = [new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.0 }],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        var inScheduler = false;
        vm.UiScheduler = action =>
        {
            inScheduler = true;
            try
            {
                action();
            }
            finally
            {
                inScheduler = false;
            }
        };

        await vm.RefreshCommand.ExecuteAsync();
        var offScheduler = 0;
        ((INotifyCollectionChanged)vm.StepDetails).CollectionChanged +=
            (_, _) =>
            {
                if (!inScheduler)
                {
                    Interlocked.Increment(ref offScheduler);
                }
            };

        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        Assert.NotEmpty(vm.StepDetails);
        Assert.Equal(0, offScheduler);
    }
}
