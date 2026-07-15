using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Plans;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Plans;

public sealed class SuiteRegressionTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Plans", fileName);

    private static (SuiteEngine Engine, FileRunStore RunStore, FileSuiteRunStore SuiteStore) Create(TempDataDirectory temp)
    {
        var settings = new AppSettings
        {
            UseMockVisa = true,
            DefaultVisaResource = "MOCK::INSTR0",
            Instruments =
            [
                new VisaInstrument { Id = "instr0", DisplayName = "Mock", Resource = "MOCK::INSTR0", Enabled = true },
            ],
        };
        var gate = new VisaSessionGate();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var suiteStore = new FileSuiteRunStore(runStore, temp.RunsDirectory);
        var runControl = new RunControl(gate);
        var testEngine = new TestEngine(
            new MockVisaSessionFactory(gate),
            runStore,
            new MeasurementAcquisition(),
            runControl,
            gate,
            new AnalyzeAlgorithmResolver([new MeanGteAnalyzeAlgorithm()]));
        var suiteEngine = new SuiteEngine(testEngine, suiteStore, settings);
        return (suiteEngine, runStore, suiteStore);
    }

    [Fact]
    public async Task Sample_suite_passes_sequentially()
    {
        using var temp = new TempDataDirectory();
        var (engine, _, _) = Create(temp);
        var suite = await new PlanLoader().LoadSampleSuiteAsync();
        var run = await engine.ExecuteAsync(suite);
        Assert.Equal(RunResult.Passed, run.Result);
        Assert.Equal(2, run.PlanRuns.Count);
    }

    [Fact]
    public async Task Suite_fail_stops_remaining_plans()
    {
        using var temp = new TempDataDirectory();
        var (engine, _, _) = Create(temp);
        var suite = await new PlanLoader().LoadSuiteFromFileAsync(FixturePath("suite-assert-fail.json"));
        var run = await engine.ExecuteAsync(suite);
        Assert.Equal(RunResult.Failed, run.Result);
        Assert.Single(run.PlanRuns);
    }

    [Fact]
    public async Task Suite_cancel_mid_run()
    {
        using var temp = new TempDataDirectory();
        var (engine, _, _) = Create(temp);
        var suite = await new PlanLoader().LoadSuiteFromFileAsync(FixturePath("suite-cancel.json"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var run = await engine.ExecuteAsync(suite, cancellationToken: cts.Token);
        Assert.Equal(RunResult.Cancelled, run.Result);
    }

    [Fact]
    public async Task Suite_serialization_round_trip()
    {
        var suite = await new PlanLoader().LoadSampleSuiteAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(suite, Core.Serialization.AppJsonContext.Default.TestSuite);
        var again = System.Text.Json.JsonSerializer.Deserialize(json, Core.Serialization.AppJsonContext.Default.TestSuite);
        Assert.NotNull(again);
        Assert.Equal(suite.Name, again!.Name);
        Assert.Equal(suite.Plans.Count, again.Plans.Count);
        Assert.Equal(suite.Plans[0].Steps.Count, again.Plans[0].Steps.Count);
    }
}
