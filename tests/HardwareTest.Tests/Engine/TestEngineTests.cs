using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Engine;

public sealed class TestEngineHappyPathTests
{
    [Fact]
    public async Task Sample_plan_executes_successfully_with_mock_visa()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = await new Core.Plans.PlanLoader().LoadSampleAsync();

        var run = await engine.ExecuteAsync(plan);

        Assert.Equal(RunResult.Passed, run.Result);
        Assert.True(run.Samples.Count > 0);
        Assert.True(File.Exists(Path.Combine(runStore.GetRunDirectory(run.RunId), "run.json")));
    }
}

public sealed class TestEngineCancelTests
{
    [Fact]
    public async Task Cancel_during_delay_marks_cancelled_and_persists_run()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder().Open().Delay(5000).Build();

        using var cts = new CancellationTokenSource();
        var execute = engine.ExecuteAsync(plan, cancellationToken: cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        var run = await execute;
        Assert.Equal(RunResult.Cancelled, run.Result);
        Assert.NotNull(await runStore.LoadAsync(run.RunId));
    }

    [Fact]
    public async Task Cancel_during_acquire_marks_cancelled()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder().Open().Acquire(samples: 500, intervalMs: 20).Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var run = await engine.ExecuteAsync(plan, cancellationToken: cts.Token);
        Assert.Equal(RunResult.Cancelled, run.Result);
    }
}

public sealed class TestEngineAssertTests
{
    [Fact]
    public async Task Failing_assert_stops_and_marks_failed()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Acquire(samples: 4, intervalMs: 0)
            .Assert("channel:VDC:mean", "gt", 1000)
            .Write("SHOULD_NOT_RUN")
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Failed, run.Result);
        Assert.DoesNotContain(run.Steps, s => s.Message == "SHOULD_NOT_RUN");
        Assert.Contains(run.Steps, s => s.StepType == nameof(Core.Plans.AssertStep) && !s.Passed);
    }

    [Fact]
    public async Task Variable_assert_can_pass()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Query("READ?", storeAs: "v")
            .Assert("v", "gte", 0)
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Passed, run.Result);
        Assert.True(run.Variables.ContainsKey("v"));
    }

    [Fact]
    public async Task Bad_assert_operator_marks_error()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Acquire(samples: 2, intervalMs: 0)
            .Assert("channel:VDC:mean", "bogus", 0)
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Error, run.Result);
    }
}

public sealed class TestEngineErrorPathTests
{
    [Fact]
    public async Task Write_without_open_marks_error()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder().Write("*RST").Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Error, run.Result);
        Assert.Contains("session", run.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_open_replaces_session()
    {
        using var temp = new TempDataDirectory();
        var factory = new SpyVisaSessionFactory();
        var gate = new VisaSessionGate();
        var runControl = new RunControl(gate);
        var engine = new TestEngine(
            factory,
            new FileRunStore(temp.RunsDirectory),
            new MeasurementAcquisition(),
            runControl,
            gate,
            new AnalyzeAlgorithmResolver([new MeanGteAnalyzeAlgorithm()]));
        var plan = new TestPlanBuilder()
            .Open("MOCK::A")
            .Open("MOCK::B")
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Passed, run.Result);
        Assert.Equal("MOCK::B", run.Resource);
        Assert.Equal("MOCK::B", factory.LastSession?.ResourceName);
    }
}
