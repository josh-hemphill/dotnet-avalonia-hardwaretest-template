using HardwareTest.Core.Runs;
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
}
