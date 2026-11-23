using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Instruments;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.Tests.OpenTap;

internal static class AlphaDevicePlanFixture
{
    internal static readonly (string Driver, string Operation, string Arguments, string Query, string Reply, string Unit)[] Cases =
    [
        ("keysight.34461a", "DmmMeasureVoltageDc", "range=10", ":MEAS:VOLT:DC? 1e1", "1.25", "V"),
        ("keithley.2602b", "DmmMeasureVoltageDc", "", ":MEAS:VOLT:DC?", "2.5", "V"),
        ("rigol.ds1054z", "OscilloscopeMeasureVpp", "channel=1", ":MEASure:VPP? CHAN1", "3.3", "V"),
        ("siglent.sdg2042x", "FunctionGeneratorReadFrequency", "", ":SOUR:FREQ?", "1000", "Hz"),
        ("tektronix.fca3100", "CounterMeasureFrequency", "", ":MEASure:FREQuency?", "1000000", "Hz")
    ];

    internal static string SavePlan(string directory)
    {
        var plan = new TestPlan();
        var group = new TestGroupStep { Name = "Mixed vendor measurements" };
        foreach (var (Driver, Operation, Arguments, _, _, _) in Cases)
        {
            var instrument = new RegisteredInstrument { Name = Driver, DriverId = Driver, VisaAddress = "MOCK::" + Driver };
            var step = new DeviceOperationStep { Name = Driver + " measurement", Instrument = instrument, Operation = Operation, Arguments = Arguments, Channel = Driver };
            _ = OpenTapMixinAttach.AttachPresentation(step, Driver, PresentationDisplayRoles.Scalar, "");
            group.ChildTestSteps.Add(step);
        }
        plan.ChildTestSteps.Add(group);
        var path = Path.Combine(directory, "mixed-alpha.TapPlan");
        plan.Save(path);
        return path;
    }

    internal static string SavePausePlan(string directory)
    {
        var device = new RegisteredInstrument { Name = "Pause test DMM", DriverId = "keysight.34461a", VisaAddress = "MOCK::PAUSE", IoTimeoutMilliseconds = 30000 };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "Held voltage", Instrument = device, Operation = "DmmMeasureVoltageDc", Channel = "held.voltage" });
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "Following current", Instrument = device, Operation = "DmmMeasureCurrentDc", Channel = "following.current" });
        var path = Path.Combine(directory, "alpha-pause.TapPlan"); plan.Save(path);
        return path;
    }

    internal static string SaveOverloadPlan(string directory, bool overload)
    {
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new DeviceOperationStep
        {
            Name = "Commissioned voltage",
            Channel = "agilent.voltage",
            Operation = "DmmMeasureVoltageDc",
            LimitHigh = 0.5,
            Instrument = new RegisteredInstrument { Name = "Qualified Agilent DMM", DriverId = "keysight.34401a", Commissioning = "GpibScpiConfirmed", VisaAddress = overload ? "MOCK::AGILENT-OVERLOAD" : "MOCK::AGILENT-ZERO" }
        });
        var path = Path.Combine(directory, overload ? "alpha-overload.TapPlan" : "alpha-zero.TapPlan"); plan.Save(path); return path;
    }

    internal static string ReadPdfText(string path)
    {
        using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(path));
        return string.Join("\n", Enumerable.Range(1, pdf.GetNumberOfPages()).Select(index => iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(pdf.GetPage(index))));
    }

    internal sealed class QualifiedDmmBroker(bool overload) : IVisaBroker
    {
        private readonly bool _overload = overload;
        public int Opens; public int Closes; public int Queries;
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            Opens++; return Task.FromResult<IVisaSession>(new Lease(this, resourceName));
        }
        private sealed class Lease(QualifiedDmmBroker owner, string resource) : IVisaSession
        {
            public string ResourceName => resource;
            public int IoTimeoutMilliseconds { get; set; }
            public Task WriteAsync(string command, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write: " + command);
            public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
            {
                if (command != ":MEAS:VOLT:DC? DEF,DEF") throw new InvalidOperationException("Unexpected query: " + command);
                owner.Queries++; return Task.FromResult(owner._overload ? "+9.90000000E+37" : "+0.00000000E+00");
            }
            public ValueTask DisposeAsync() { owner.Closes++; return ValueTask.CompletedTask; }
        }
    }

    /// Test-owned complete-message transport: hold the first instrument reply while
    /// a real host/view requests Pause. No timing or synchronization hooks ship in the app.
    internal sealed class HeldReplyBroker : IVisaBroker
    {
        public int Opens;
        public int Closes;
        public int Queries;
        public TaskCompletionSource Entered = NewSignal();
        public TaskCompletionSource Release = NewSignal();
        public TaskCompletionSource Returned = NewSignal();
        public TaskCompletionSource Following = NewSignal();
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm()
        {
            Queries = 0; Entered = NewSignal(); Release = NewSignal(); Returned = NewSignal(); Following = NewSignal();
        }
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref Opens);
            return Task.FromResult<IVisaSession>(new Lease(this, resourceName));
        }
        private sealed class Lease(HeldReplyBroker owner, string resource) : IVisaSession
        {
            private int _disposed;
            public string ResourceName => resource;
            public int IoTimeoutMilliseconds { get; set; }
            public Task WriteAsync(string command, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write: " + command);
            public async Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref owner.Queries) == 1)
                {
                    if (command != ":MEAS:VOLT:DC?") throw new InvalidOperationException("Unexpected first query: " + command);
                    _ = owner.Entered.TrySetResult();
                    await owner.Release.Task.WaitAsync(cancellationToken);
                    _ = owner.Returned.TrySetResult();
                    return "1.25";
                }
                _ = owner.Following.TrySetResult();
                if (command != ":MEAS:CURR:DC?") throw new InvalidOperationException("Unexpected following query: " + command);
                return "0.005";
            }
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) throw new InvalidOperationException("Lease closed twice.");
                _ = Interlocked.Increment(ref owner.Closes); return ValueTask.CompletedTask;
            }
        }
    }

}
