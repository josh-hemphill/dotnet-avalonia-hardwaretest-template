using System.Text.Json;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Plans;

public sealed class PlanRegressionTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Plans", fileName);

    private static TestEngine CreateEngine(TempDataDirectory temp)
    {
        var gate = new VisaSessionGate();
        return TestEngineFactory.CreateEngine(temp);
    }

    [Fact]
    public async Task Assert_pass_fixture_passes()
    {
        using var temp = new TempDataDirectory();
        var plan = await new PlanLoader().LoadFromFileAsync(FixturePath("assert-pass.json"));
        var run = await CreateEngine(temp).ExecuteAsync(plan);
        Assert.Equal(RunResult.Passed, run.Result);
        await AssertRunJsonShapeAsync(temp, run.RunId);
    }

    [Fact]
    public async Task Assert_fail_fixture_fails_and_stops_early()
    {
        using var temp = new TempDataDirectory();
        var plan = await new PlanLoader().LoadFromFileAsync(FixturePath("assert-fail.json"));
        var run = await CreateEngine(temp).ExecuteAsync(plan);

        Assert.Equal(RunResult.Failed, run.Result);
        Assert.DoesNotContain(run.Steps, s => s.Message == "SHOULD_NOT_RUN");
        Assert.Equal(3, run.Steps.Count);
        await AssertRunJsonShapeAsync(temp, run.RunId);
    }

    [Fact]
    public async Task Cancel_friendly_long_acquire_cancels()
    {
        using var temp = new TempDataDirectory();
        var plan = await new PlanLoader().LoadFromFileAsync(FixturePath("cancel-friendly-long-acquire.json"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var run = await CreateEngine(temp).ExecuteAsync(plan, cancellationToken: cts.Token);
        Assert.Equal(RunResult.Cancelled, run.Result);
        await AssertRunJsonShapeAsync(temp, run.RunId);
    }

    [Fact]
    public async Task Query_store_assert_fixture_passes()
    {
        using var temp = new TempDataDirectory();
        var plan = await new PlanLoader().LoadFromFileAsync(FixturePath("query-store-assert.json"));
        var run = await CreateEngine(temp).ExecuteAsync(plan);
        Assert.Equal(RunResult.Passed, run.Result);
        Assert.True(run.Variables.ContainsKey("v"));
        await AssertRunJsonShapeAsync(temp, run.RunId);
    }

    [Fact]
    public async Task Malformed_empty_steps_throws_from_loader()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PlanLoader().LoadFromFileAsync(FixturePath("malformed-empty-steps.json")));
        Assert.Contains("at least one step", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertRunJsonShapeAsync(TempDataDirectory temp, string runId)
    {
        var path = Path.Combine(temp.RunsDirectory, runId, "run.json");
        Assert.True(File.Exists(path), $"Expected run.json at {path}");
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("runId", out _));
        Assert.True(root.TryGetProperty("planName", out _));
        Assert.True(root.TryGetProperty("startedAt", out _));
        Assert.True(root.TryGetProperty("result", out _));
        Assert.True(root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array);
        Assert.True(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array);

        var reloaded = await JsonSerializer.DeserializeAsync(File.OpenRead(path), AppJsonContext.Default.TestRunRecord);
        Assert.NotNull(reloaded);
        Assert.Equal(runId, reloaded!.RunId);
    }
}
