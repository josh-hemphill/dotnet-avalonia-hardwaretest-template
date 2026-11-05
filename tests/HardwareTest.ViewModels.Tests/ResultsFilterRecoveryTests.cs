using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsFilterRecoveryTests
{
    [Fact]
    public async Task Empty_history_and_no_matches_offer_different_recovery_without_changing_saved_evidence()
    {
        using var store = new FakeRunStore();
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.False(vm.HasStoredRuns);
        Assert.Equal("No saved runs yet", vm.EmptyResultsTitle);
        Assert.Contains("Run page", vm.EmptyResultsHint);
        var record = new TestRunRecord
        {
            RunId = "filter-evidence",
            PlanId = "sample",
            PlanName = "Acceptance",
            DutSerial = "SERIAL-42",
            OperatorName = "Operator",
            Result = RunResult.Passed,
            StartedAt = DateTimeOffset.UtcNow,
            Samples = [new StoredSample { Channel = "VDC", Value = 5 }]
        };
        store.Seed(record);
        var before = JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SearchText = "no matching serial";
        vm.ResultFilter = "Failed";
        vm.OperatorFilter = "no matching operator";
        vm.DateFilter = ResultsViewModel.DateToday;
        Assert.Empty(vm.Runs);
        Assert.True(vm.HasStoredRuns);
        Assert.True(vm.HasActiveFilters);
        Assert.Equal("No matching runs", vm.EmptyResultsTitle);
        Assert.Contains("search or filters", vm.EmptyResultsHint);
        Assert.Contains("active", vm.FilterHeading);
        await vm.ClearFiltersCommand.ExecuteAsync();
        Assert.False(vm.HasActiveFilters);
        Assert.Equal("Filters", vm.FilterHeading);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(ResultsViewModel.AllFilter, vm.ResultFilter);
        Assert.Equal(ResultsViewModel.AllFilter, vm.OperatorFilter);
        Assert.Equal(ResultsViewModel.DateAll, vm.DateFilter);
        Assert.Equal("filter-evidence", Assert.Single(vm.Runs).RunId);
        var persisted = Assert.IsType<TestRunRecord>(await store.LoadAsync("filter-evidence"));
        Assert.Equal(before, JsonSerializer.Serialize(persisted, AppJsonContext.Default.TestRunRecord));
    }
}
