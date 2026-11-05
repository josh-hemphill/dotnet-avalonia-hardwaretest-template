using System.Globalization;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsRunIdentityTests
{
    [Theory]
    [InlineData(RunResult.Passed)]
    [InlineData(RunResult.Failed)]
    [InlineData(RunResult.Cancelled)]
    [InlineData(RunResult.Error)]
    [InlineData(RunResult.Unknown)]
    public async Task Direct_open_preserves_verdict_and_exposes_the_exact_saved_identity_with_unambiguous_times(RunResult result)
    {
        using var store = new FakeRunStore();
        var record = new TestRunRecord
        {
            RunId = "identity-56789012345678901234567890",
            PlanId = "controller-production-test-v4",
            PlanName = "Controller acceptance",
            SessionId = "session-full-12345678901234567890",
            DutSerial = "DUT-742",
            DutPartNumber = "PART-27",
            DutRevision = "C",
            OperatorName = "Review technician",
            StartedAt = new DateTimeOffset(2026, 11, 4, 10, 5, 3, TimeSpan.FromHours(-8)),
            CompletedAt = new DateTimeOffset(2026, 11, 4, 10, 7, 9, TimeSpan.FromHours(-8)),
            Result = result,
            ExecutionScope = RunExecutionScopes.SelectedStep,
            SelectedStepPath = "Controller acceptance/Diagnostic voltage",
        };
        store.Seed(record);
        var vm = new ResultsViewModel(store, new FakeReportService()) { UiScheduler = action => action() };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await vm.OpenRunByIdAsync(record.RunId);
        Assert.True(vm.ShowDetail);
        Assert.Contains($"({result})", vm.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(vm.RunIdentityText), changed);
        foreach (var value in new[] { record.RunId, record.PlanId, record.PlanName, record.SessionId, record.DutSerial, record.DutPartNumber,
                     record.DutRevision, record.OperatorName, record.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                     record.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture), record.ExecutionScopeText })
            Assert.Contains(value!, vm.RunIdentityText, StringComparison.Ordinal);
        Assert.Contains($"Verdict: {result}", vm.RunIdentityText, StringComparison.Ordinal);
        Assert.Same(record, await store.LoadAsync(record.RunId));
    }

    [Fact]
    public async Task Legacy_missing_identity_is_explicitly_not_recorded()
    {
        using var store = new FakeRunStore();
        store.Seed(new TestRunRecord { RunId = "legacy-identity", Result = RunResult.Unknown });
        var vm = new ResultsViewModel(store, new FakeReportService()) { UiScheduler = action => action() };
        await vm.OpenRunByIdAsync("legacy-identity");
        foreach (var field in new[] { "Program ID", "Session ID", "Part number", "Revision", "Operator", "Started at", "Completed at" })
            Assert.Contains(field + ": Not recorded", vm.RunIdentityText, StringComparison.Ordinal);
    }
}
