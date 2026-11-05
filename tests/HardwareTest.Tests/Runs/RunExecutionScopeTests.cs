using HardwareTest.Core.Runs;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class RunExecutionScopeTests
{
    [Theory]
    [InlineData(RunExecutionScopes.FullSuite, null, "Full suite")]
    [InlineData(RunExecutionScopes.SelectedStep, "Power/Acquire", "Selected step only: Power/Acquire")]
    public async Task Saved_scope_survives_load_and_list_without_becoming_a_full_suite(
        string scope, string? path, string label)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var record = new TestRunRecord
        {
            RunId = "scope-roundtrip",
            PlanName = "Controller acceptance",
            ExecutionScope = scope,
            SelectedStepPath = path,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(record);

        var loaded = Assert.IsType<TestRunRecord>(await store.LoadAsync(record.RunId));
        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(scope, loaded.ExecutionScope);
        Assert.Equal(path, loaded.SelectedStepPath);
        Assert.Equal(label, loaded.ExecutionScopeText);
        Assert.Equal(scope, listed.ExecutionScope);
        Assert.Equal(path, listed.SelectedStepPath);
        Assert.Equal(label, listed.ExecutionScopeText);
        Assert.Equal(RunResult.Passed, listed.Result);
    }

    [Fact]
    public async Task Existing_passed_record_without_scope_does_not_claim_a_full_suite()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var path = Path.Combine(store.GetRunDirectory("legacy-scope"), "run.json");
        const string original = """{"schemaVersion":4,"runId":"legacy-scope","planName":"Prior job","result":"Passed"}""";
        await File.WriteAllTextAsync(path, original);

        var loaded = Assert.IsType<TestRunRecord>(await store.LoadAsync("legacy-scope"));
        var listed = Assert.Single(await store.ListAsync());
        Assert.Null(loaded.ExecutionScope);
        Assert.Null(loaded.SelectedStepPath);
        Assert.Equal("Scope not recorded", loaded.ExecutionScopeText);
        Assert.Equal("Scope not recorded", listed.ExecutionScopeText);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }
}
