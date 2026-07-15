using HardwareTest.Core.Plans;

namespace HardwareTest.Tests.Fixtures;

/// Builds minimal declarative plans for engine tests.
public sealed class TestPlanBuilder
{
    private readonly TestPlan _plan = new()
    {
        Id = "test",
        Name = "Test Plan",
        Resource = "MOCK::0",
        Steps = [],
    };

    public TestPlanBuilder WithName(string name)
    {
        _plan.Name = name;
        return this;
    }

    public TestPlanBuilder WithResource(string? resource)
    {
        _plan.Resource = resource;
        return this;
    }

    public TestPlanBuilder Open(string resource = "MOCK::0")
    {
        _plan.Steps.Add(new OpenStep { Resource = resource });
        return this;
    }

    public TestPlanBuilder Write(string command)
    {
        _plan.Steps.Add(new WriteStep { Command = command });
        return this;
    }

    public TestPlanBuilder Query(string command, string? storeAs = null)
    {
        _plan.Steps.Add(new QueryStep { Command = command, StoreAs = storeAs });
        return this;
    }

    public TestPlanBuilder Acquire(string channel = "VDC", int samples = 8, int intervalMs = 1, string? query = "READ?")
    {
        _plan.Steps.Add(new AcquireStep
        {
            Channel = channel,
            SampleCount = samples,
            IntervalMs = intervalMs,
            QueryCommand = query,
        });
        return this;
    }

    public TestPlanBuilder Delay(int milliseconds)
    {
        _plan.Steps.Add(new DelayStep { Milliseconds = milliseconds });
        return this;
    }

    public TestPlanBuilder Assert(string source, string op, double value)
    {
        _plan.Steps.Add(new AssertStep { Source = source, Operator = op, Value = value });
        return this;
    }

    public TestPlanBuilder AddStep(PlanStep step)
    {
        _plan.Steps.Add(step);
        return this;
    }

    public TestPlanBuilder Analyze(string algorithm = "mean-gte", string channel = "VDC", double value = 0, string? storeAs = null)
    {
        _plan.Steps.Add(new AnalyzeStep
        {
            Algorithm = algorithm,
            Channel = channel,
            Value = value,
            StoreAs = storeAs,
        });
        return this;
    }

    public TestPlanBuilder SafeShutdown(params PlanStep[] steps)
    {
        _plan.SafeShutdown.AddRange(steps);
        return this;
    }

    public TestPlan Build() => _plan;
}
