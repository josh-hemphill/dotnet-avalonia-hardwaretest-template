using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Tests.Fixtures;

namespace HardwareTest.Tests;

internal static class TestEngineFactory
{
    public static (TestEngine Engine, VisaSessionGate Gate, IRunControl RunControl) Create(TempDataDirectory temp)
    {
        var gate = new VisaSessionGate();
        var runControl = new RunControl(gate);
        var algorithms = new AnalyzeAlgorithmResolver([new MeanGteAnalyzeAlgorithm()]);
        var engine = new TestEngine(
            new MockVisaSessionFactory(gate),
            new FileRunStore(temp.RunsDirectory),
            new MeasurementAcquisition(),
            runControl,
            gate,
            algorithms);
        return (engine, gate, runControl);
    }

    public static TestEngine CreateEngine(TempDataDirectory temp) => Create(temp).Engine;
}
