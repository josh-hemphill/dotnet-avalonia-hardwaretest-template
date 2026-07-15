using System.Text.Json;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Tests.Plans;

public sealed class PlanLoaderTests
{
    [Fact]
    public async Task LoadSample_returns_named_plan_with_steps()
    {
        var plan = await new PlanLoader().LoadSampleAsync();
        Assert.False(string.IsNullOrWhiteSpace(plan.Name));
        Assert.NotEmpty(plan.Steps);
    }

    [Fact]
    public async Task LoadFromFile_valid_plan_works()
    {
        var path = Path.GetTempFileName();
        try
        {
            var json = """
                       {"id":"p1","name":"FromFile","steps":[{"type":"Delay","milliseconds":1}]}
                       """;
            await File.WriteAllTextAsync(path, json);
            var plan = await new PlanLoader().LoadFromFileAsync(path);
            Assert.Equal("FromFile", plan.Name);
            Assert.Single(plan.Steps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadFromFile_empty_steps_throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"id":"p1","name":"Bad","steps":[]}""");
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PlanLoader().LoadFromFileAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadFromFile_missing_name_throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"id":"p1","name":"","steps":[{"type":"Delay","milliseconds":1}]}""");
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PlanLoader().LoadFromFileAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ListEmbeddedPlanNames_includes_sample()
    {
        var names = new PlanLoader().ListEmbeddedPlanNames();
        Assert.Contains(names, n => n.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class TestPlanSerializationTests
{
    [Fact]
    public void Round_trips_all_step_types()
    {
        var plan = new TestPlan
        {
            Id = "poly",
            Name = "Poly",
            Steps =
            [
                new OpenStep { Resource = "MOCK::0" },
                new WriteStep { Command = "*RST" },
                new QueryStep { Command = "*IDN?", StoreAs = "idn" },
                new AssertStep { Source = "v", Operator = "gte", Value = 1 },
                new AcquireStep { Channel = "VDC", SampleCount = 2, IntervalMs = 1 },
                new DelayStep { Milliseconds = 5 },
            ],
        };

        var json = JsonSerializer.Serialize(plan, AppJsonContext.Default.TestPlan);
        var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.TestPlan);
        Assert.NotNull(loaded);
        Assert.Equal(6, loaded!.Steps.Count);
        Assert.IsType<OpenStep>(loaded.Steps[0]);
        Assert.IsType<WriteStep>(loaded.Steps[1]);
        Assert.IsType<QueryStep>(loaded.Steps[2]);
        Assert.IsType<AssertStep>(loaded.Steps[3]);
        Assert.IsType<AcquireStep>(loaded.Steps[4]);
        Assert.IsType<DelayStep>(loaded.Steps[5]);
    }
}
