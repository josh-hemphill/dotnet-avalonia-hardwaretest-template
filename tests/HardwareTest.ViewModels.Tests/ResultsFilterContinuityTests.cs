using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsFilterContinuityTests
{
    [Fact]
    public async Task Rebuilding_filters_preserves_closed_selection_without_reading_or_reopening_saved_details()
    {
        var root = Path.Combine(Path.GetTempPath(), "results-filter-continuity-" + Guid.NewGuid().ToString("N"));
        var store = new CountingRunStore(new FileRunStore(root));
        try
        {
            var first = Record("continuity-first", "DUT-FIRST", "First reviewer");
            var second = Record("continuity-second", "DUT-SECOND", "Second reviewer");
            await store.SaveAsync(first);
            await store.SaveAsync(second);
            var firstPath = Path.Combine(store.GetRunDirectory(first.RunId), "run.json");
            var secondPath = Path.Combine(store.GetRunDirectory(second.RunId), "run.json");
            var firstBytes = File.ReadAllBytes(firstPath);
            var secondBytes = File.ReadAllBytes(secondPath);
            var vm = new ResultsViewModel(store, new FakeReportService()) { UiScheduler = action => action() };
            vm.SearchText = first.RunId;
            await vm.OpenRunByIdAsync(first.RunId);
            Assert.True(vm.ShowDetail);
            await vm.CloseDetailCommand.ExecuteAsync();
            Assert.Equal(first.RunId, vm.SelectedRun?.RunId);
            var readsBeforeFiltering = store.LoadCount;
            // A bound selection owner clears its selection as soon as the selected
            // row leaves ItemsSource. Restoration is maintenance, not user activation.
            vm.Runs.CollectionChanged += (_, _) =>
            {
                if (vm.SelectedRun is { } selected && !vm.Runs.Contains(selected)) vm.SelectedRun = null;
            };

            vm.SearchText = string.Empty;
            Assert.Equal(2, vm.Runs.Count);
            AssertClosedSelection(first.RunId);
            foreach (var query in new[] { "c", "continuity", "continuity-f", first.RunId })
            {
                vm.SearchText = query;
                AssertClosedSelection(first.RunId);
            }
            vm.ResultFilter = nameof(RunResult.Passed);
            AssertClosedSelection(first.RunId);
            vm.OperatorFilter = first.OperatorName!;
            AssertClosedSelection(first.RunId);
            vm.DateFilter = ResultsViewModel.DateToday;
            AssertClosedSelection(first.RunId);
            await vm.ClearFiltersCommand.ExecuteAsync();
            AssertClosedSelection(first.RunId);
            vm.SearchText = second.RunId;
            Assert.Single(vm.Runs);
            Assert.Null(vm.SelectedRun);
            Assert.False(vm.ShowDetail);
            Assert.Equal(readsBeforeFiltering, store.LoadCount);

            // A subsequent genuine selection still opens the intended record.
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.ShowDetail) && vm.ShowDetail) opened.TrySetResult();
            };
            vm.SelectedRun = Assert.Single(vm.Runs);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(vm.ShowDetail);
            Assert.Equal(second.RunId, vm.OpenedRun?.RunId);
            Assert.True(store.LoadCount > readsBeforeFiltering);
            Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
            Assert.Equal(secondBytes, File.ReadAllBytes(secondPath));

            void AssertClosedSelection(string runId)
            {
                Assert.False(vm.ShowDetail);
                Assert.Equal(runId, vm.SelectedRun?.RunId);
                Assert.Equal(readsBeforeFiltering, store.LoadCount);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static TestRunRecord Record(string id, string dut, string technician)
        => new()
        {
            RunId = id,
            PlanId = "filter-continuity",
            PlanName = "Filter continuity acceptance",
            ExecutionScope = RunExecutionScopes.FullSuite,
            DutSerial = dut,
            OperatorName = technician,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };

    private sealed class CountingRunStore(IRunStore inner) : IRunStore
    {
        internal int LoadCount { get; private set; }
        public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
            => inner.SaveAsync(run, cancellationToken);
        public Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return inner.LoadAsync(runId, cancellationToken);
        }
        public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
            => inner.ListAsync(cancellationToken);
        public string GetRunDirectory(string runId) => inner.GetRunDirectory(runId);
    }
}
