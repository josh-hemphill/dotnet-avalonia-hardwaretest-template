using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class FileSuiteRunStoreTests
{
    [Fact]
    public async Task Current_suite_and_children_round_trip_including_suite_pdf()
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        var child = new TestRunRecord { RunId = "child", PlanName = "Plan", Result = RunResult.Passed };
        var suite = new SuiteRunRecord { SuiteRunId = "suite", PlanRuns = [child], ReportPdfPath = "suite.pdf" };
        await suites.SaveAsync(suite);

        var loaded = await suites.LoadAsync(suite.SuiteRunId);
        Assert.NotNull(loaded);
        Assert.Equal(SchemaVersions.SuiteRunRecord, loaded.SchemaVersion);
        Assert.Equal(SchemaVersions.SuiteRunRecord, loaded.StoredSchemaVersion);
        Assert.False(loaded.IsSchemaReadOnly);
        Assert.Equal("suite.pdf", loaded.ReportPdfPath);
        Assert.Equal(SchemaVersions.TestRunRecord, Assert.Single(loaded.PlanRuns).SchemaVersion);
        Assert.Equal("Plan", (await runs.LoadAsync("child"))?.PlanName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":3}")]
    public async Task Embedded_unsupported_run_header_rejects_suite_before_initializers_or_backup_recovery(string child)
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"suiteRunId\":\"suite\",\"planRuns\":[" + child + "]}");
        var primary = File.ReadAllBytes(path);
        var backup = JsonSerializer.SerializeToUtf8Bytes(new SuiteRunRecord { SuiteRunId = "suite" }, AppJsonContext.Default.SuiteRunRecord);
        File.WriteAllBytes(path + ".bak", backup);

        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.LoadAsync("suite"));
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.SaveAsync(new SuiteRunRecord { SuiteRunId = "suite" }));
        Assert.Equal(primary, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public async Task Embedded_future_run_makes_suite_read_only_and_blocks_fresh_overwrite()
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"suiteRunId\":\"suite\",\"planRuns\":[{\"schemaVersion\":999,\"runId\":\"child\"}]}");
        var before = File.ReadAllBytes(path);
        var loaded = await suites.LoadAsync("suite");
        Assert.NotNull(loaded);
        Assert.True(loaded.IsSchemaReadOnly);
        Assert.True(Assert.Single(loaded.PlanRuns).IsSchemaReadOnly);
        Assert.Equal(999, loaded.PlanRuns[0].StoredSchemaVersion);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(loaded));
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(new SuiteRunRecord { SuiteRunId = "suite" }));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(999)]
    public async Task Invalid_child_object_is_rejected_before_any_child_is_saved(int version)
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        await runs.SaveAsync(new TestRunRecord { RunId = "first", PlanName = "original" });
        var firstPath = Path.Combine(runs.GetRunDirectory("first"), "run.json");
        var firstBytes = File.ReadAllBytes(firstPath);
        var invalid = new TestRunRecord { RunId = "invalid", SchemaVersion = version };
        var candidate = new SuiteRunRecord
        {
            SuiteRunId = "suite",
            PlanRuns = [new TestRunRecord { RunId = "first", PlanName = "changed" }, invalid],
        };
        if (version > SchemaVersions.TestRunRecord) await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(candidate));
        else await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.SaveAsync(candidate));
        Assert.Equal(version, invalid.SchemaVersion);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
        Assert.False(File.Exists(firstPath + ".bak"));
        Assert.False(File.Exists(Path.Combine(runs.GetRunDirectory("invalid"), "run.json")));
        Assert.False(File.Exists(Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json")));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(999)]
    public async Task Blocked_child_destination_is_rejected_before_any_child_is_rewritten(int version)
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        await runs.SaveAsync(new TestRunRecord { RunId = "first", PlanName = "original" });
        var firstPath = Path.Combine(runs.GetRunDirectory("first"), "run.json");
        var firstBytes = File.ReadAllBytes(firstPath);
        var blockedPath = Path.Combine(runs.GetRunDirectory("blocked"), "run.json");
        File.WriteAllText(blockedPath, $"{{\"schemaVersion\":{version},\"runId\":\"blocked\"}}");
        var blockedBytes = File.ReadAllBytes(blockedPath);
        var candidate = new SuiteRunRecord
        {
            SuiteRunId = "suite",
            PlanRuns = [new TestRunRecord { RunId = "first", PlanName = "changed" }, new TestRunRecord { RunId = "blocked" }],
        };
        if (version > SchemaVersions.TestRunRecord) await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(candidate));
        else await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.SaveAsync(candidate));
        Assert.Equal(firstBytes, File.ReadAllBytes(firstPath));
        Assert.Equal(blockedBytes, File.ReadAllBytes(blockedPath));
        Assert.False(File.Exists(firstPath + ".bak"));
        Assert.False(File.Exists(Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json")));
    }

    [Fact]
    public async Task Corrupt_suite_restores_valid_current_backup_with_current_children()
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json");
        File.WriteAllText(path, "{corrupt");
        var backup = JsonSerializer.SerializeToUtf8Bytes(new SuiteRunRecord
        {
            SuiteRunId = "suite",
            ReportPdfPath = "suite.pdf",
            PlanRuns = [new TestRunRecord { RunId = "child" }],
        }, AppJsonContext.Default.SuiteRunRecord);
        File.WriteAllBytes(path + ".bak", backup);
        var loaded = await suites.LoadAsync("suite");
        Assert.NotNull(loaded);
        Assert.Equal("suite.pdf", loaded.ReportPdfPath);
        Assert.Equal("child", Assert.Single(loaded.PlanRuns).RunId);
        Assert.Equal(backup, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(999)]
    public async Task Corrupt_suite_cannot_restore_backup_with_noncurrent_embedded_run(int childVersion)
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json");
        File.WriteAllText(path, "{corrupt");
        File.WriteAllText(path + ".bak", $"{{\"schemaVersion\":1,\"suiteRunId\":\"suite\",\"planRuns\":[{{\"schemaVersion\":{childVersion},\"runId\":\"child\"}}]}}");
        var primary = File.ReadAllBytes(path);
        var backup = File.ReadAllBytes(path + ".bak");
        if (childVersion > SchemaVersions.TestRunRecord)
            await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.LoadAsync("suite"));
        else
            await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.LoadAsync("suite"));
        Assert.Equal(primary, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }
    [Fact]
    public async Task Future_child_with_invalid_typed_data_cannot_trigger_current_backup_replacement()
    {
        using var temp = new TempDataDirectory();
        var suites = new FileSuiteRunStore(new FileRunStore(temp.RunsDirectory), temp.RunsDirectory);
        var path = Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"planRuns\":[{\"schemaVersion\":999,\"samples\":{}}]}");
        var before = File.ReadAllBytes(path);
        var backup = JsonSerializer.SerializeToUtf8Bytes(new SuiteRunRecord { SuiteRunId = "suite" }, AppJsonContext.Default.SuiteRunRecord);
        File.WriteAllBytes(path + ".bak", backup);
        await Assert.ThrowsAsync<JsonException>(() => suites.LoadAsync("suite"));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":3}", "unsupported")]
    [InlineData("{\"schemaVersion\":999}", "future")]
    [InlineData("{\"schemaVersion\":4,\"samples\":{}}", "corrupt")]
    public async Task Sole_blocked_child_backup_rejects_suite_before_rewriting_any_other_child(string backup, string failure)
    {
        using var temp = new TempDataDirectory();
        var runs = new FileRunStore(temp.RunsDirectory);
        var suites = new FileSuiteRunStore(runs, temp.RunsDirectory);
        await runs.SaveAsync(new TestRunRecord { RunId = "first", PlanName = "original" });
        var firstPath = Path.Combine(runs.GetRunDirectory("first"), "run.json");
        var first = File.ReadAllBytes(firstPath);
        var blockedPath = Path.Combine(runs.GetRunDirectory("blocked"), "run.json");
        File.WriteAllText(blockedPath + ".bak", backup);
        var before = File.ReadAllBytes(blockedPath + ".bak");
        var suite = new SuiteRunRecord
        {
            SuiteRunId = "suite",
            PlanRuns = [new TestRunRecord { RunId = "first", PlanName = "changed" }, new TestRunRecord { RunId = "blocked" }],
        };
        if (failure == "future") await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(suite));
        else if (failure == "unsupported") await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => suites.SaveAsync(suite));
        else await Assert.ThrowsAnyAsync<JsonException>(() => suites.SaveAsync(suite));
        Assert.Equal(first, File.ReadAllBytes(firstPath));
        Assert.Equal(before, File.ReadAllBytes(blockedPath + ".bak"));
        Assert.False(File.Exists(blockedPath));
        Assert.False(File.Exists(firstPath + ".bak"));
        Assert.False(File.Exists(Path.Combine(suites.GetSuiteRunDirectory("suite"), "suite-run.json")));
    }

}
