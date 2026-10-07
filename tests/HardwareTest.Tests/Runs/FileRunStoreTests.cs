using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Runs;

public sealed class FileRunStoreTests
{
    [Fact]
    public async Task Save_load_list_round_trip_orders_by_started_desc()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);

        var older = new TestRunRecord
        {
            RunId = "run-old",
            PlanName = "A",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Result = RunResult.Passed,
        };
        var newer = new TestRunRecord
        {
            RunId = "run-new",
            PlanName = "B",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Failed,
        };

        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var loaded = await store.LoadAsync("run-new");
        Assert.NotNull(loaded);
        Assert.Equal("B", loaded!.PlanName);

        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("run-new", list[0].RunId);
        Assert.Equal("run-old", list[1].RunId);
    }

    [Fact]
    public async Task Missing_run_returns_null()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        Assert.Null(await store.LoadAsync("does-not-exist"));
    }

    [Fact]
    public async Task Invalid_filename_chars_are_sanitized()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord
        {
            RunId = "bad:id/name",
            PlanName = "X",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(run);
        var dir = store.GetRunDirectory(run.RunId);
        Assert.True(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(dir, "run.json")));
        Assert.DoesNotContain(':', Path.GetFileName(dir));
    }

    [Fact]
    public async Task AppVersion_round_trips_through_file_store()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord
        {
            RunId = "run-versioned",
            PlanName = "Plan",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            AppVersion = "0.1.0+abc1234.20260728220000",
            AppCommitSha = "abc1234",
        };

        await store.SaveAsync(run);
        var loaded = await store.LoadAsync(run.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(run.AppVersion, loaded!.AppVersion);
        Assert.Equal(run.AppCommitSha, loaded.AppCommitSha);
    }
    [Theory]
    [InlineData(3)]
    [InlineData(999)]
    public async Task Fresh_current_run_cannot_create_primary_over_a_sole_noncurrent_backup(int version)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var path = Path.Combine(store.GetRunDirectory("protected"), "run.json");
        File.WriteAllText(path + ".bak", $"{{\"schemaVersion\":{version},\"runId\":\"protected\"}}");
        var backup = File.ReadAllBytes(path + ".bak");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var candidate = new TestRunRecord { RunId = "protected", PlanName = "new" };
            if (version > SchemaVersions.TestRunRecord)
                await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(candidate));
            else
                await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => store.SaveAsync(candidate));
            Assert.False(File.Exists(path));
            Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }
    }

}
