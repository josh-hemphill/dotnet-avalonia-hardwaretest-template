using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Engine;

public sealed class RunControlAndSafetyTests
{
    [Fact]
    public async Task Pause_blocks_until_resume()
    {
        using var temp = new TempDataDirectory();
        var (engine, _, runControl) = TestEngineFactory.Create(temp);
        var plan = new TestPlanBuilder().Open().Delay(2000).Build();

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        var execute = engine.ExecuteAsync(plan, cancellationToken: cts.Token);
        await Task.Delay(30);
        runControl.Pause();
        await Task.Delay(200);
        Assert.False(execute.IsCompleted);
        runControl.Resume();
        cts.CancelAfter(50);
        var run = await execute;
        Assert.Equal(RunResult.Cancelled, run.Result);
        runControl.DetachRun();
    }

    [Fact]
    public async Task Safety_stop_runs_safe_shutdown_and_marks_cancelled()
    {
        using var temp = new TempDataDirectory();
        var (engine, _, runControl) = TestEngineFactory.Create(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Delay(5000)
            .SafeShutdown(new WriteStep { Command = "*RST" })
            .Build();

        using var cts = new CancellationTokenSource();
        runControl.AttachRun(cts);
        var execute = engine.ExecuteAsync(plan, cancellationToken: cts.Token);
        await Task.Delay(40);
        runControl.RequestSafetyStop();
        var run = await execute;
        runControl.DetachRun();

        Assert.Equal(RunResult.Cancelled, run.Result);
        Assert.Equal("Safety stop", run.ErrorMessage);
        Assert.Contains(run.Steps, s => s.StepType == nameof(WriteStep) && s.Message == "*RST");
    }
}

public sealed class RoleBindingTests
{
    [Fact]
    public void Station_binding_resolves_role_to_resource()
    {
        var settings = new AppSettings
        {
            DefaultVisaResource = "MOCK::FALLBACK",
            Instruments =
            [
                new VisaInstrument { Id = "instr0", DisplayName = "Mock", Resource = "MOCK::INSTR0", Enabled = true },
            ],
            StationBindings = [new StationBinding { Role = "dmm", InstrumentId = "instr0" }],
        };

        Assert.Equal("MOCK::INSTR0", InstrumentResourceResolver.Resolve("dmm", settings));
    }

    [Fact]
    public void Suite_role_map_resolves_without_station_binding()
    {
        var settings = new AppSettings
        {
            Instruments =
            [
                new VisaInstrument { Id = "instr0", DisplayName = "Mock", Resource = "MOCK::INSTR0", Enabled = true },
            ],
        };
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dmm"] = "instr0" };
        Assert.Equal("MOCK::INSTR0", InstrumentResourceResolver.Resolve("dmm", settings, map));
    }
}

public sealed class AnalyzeStepTests
{
    [Fact]
    public async Task Mean_gte_analyze_passes()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Acquire(samples: 4, intervalMs: 0)
            .Analyze("mean-gte", "VDC", 0, "meanVdc")
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Passed, run.Result);
        Assert.Contains(run.Steps, s => s.StepType == nameof(AnalyzeStep) && s.Passed);
        Assert.True(run.Variables.ContainsKey("meanVdc"));
    }

    [Fact]
    public async Task Mean_gte_analyze_fails_when_threshold_high()
    {
        using var temp = new TempDataDirectory();
        var engine = TestEngineFactory.CreateEngine(temp);
        var plan = new TestPlanBuilder()
            .Open()
            .Acquire(samples: 4, intervalMs: 0)
            .Analyze("mean-gte", "VDC", 1000)
            .Build();

        var run = await engine.ExecuteAsync(plan);
        Assert.Equal(RunResult.Failed, run.Result);
    }
}
