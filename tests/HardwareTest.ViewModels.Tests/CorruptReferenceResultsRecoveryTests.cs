using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class CorruptReferenceResultsRecoveryTests
{
    [Fact]
    public async Task Opening_a_newly_corrupted_result_is_actionable_and_another_result_still_opens()
    {
        var root = Path.Combine(Path.GetTempPath(), "reference-result-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileRunStore(root);
            await store.SaveAsync(new TestRunRecord { RunId = "corrupted", PlanName = "Damaged evidence" });
            await store.SaveAsync(new TestRunRecord { RunId = "intact", PlanName = "Intact evidence" });
            var vm = new ResultsViewModel(store, new FakeReportService()) { UiScheduler = action => action() };
            await vm.RefreshCommand.ExecuteAsync();
            var selected = Assert.Single(vm.Runs, run => run.RunId == "corrupted");
            var path = Path.Combine(store.GetRunDirectory("corrupted"), "run.json");
            await File.WriteAllTextAsync(path, $"{{\"schemaVersion\":{SchemaVersions.TestRunRecord},\"runId\":\"corrupted\",\"referenceRules\":null}}");
            var bytes = await File.ReadAllBytesAsync(path);
            vm.SelectedRun = selected;
            await vm.OpenCommand.ExecuteAsync();
            Assert.Null(vm.OpenedRun);
            Assert.Contains("Preserve the original", vm.Status, StringComparison.Ordinal);
            await vm.OpenRunByIdAsync("intact");
            Assert.Equal("intact", vm.OpenedRun?.RunId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
