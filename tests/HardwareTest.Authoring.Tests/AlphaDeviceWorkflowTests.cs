using System.IO.Compression;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Instruments;
using InstrumentComponents.Drivers;
using InstrumentComponents.Drivers.Catalog;
using InstrumentComponents.Scpi;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AlphaDeviceWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-alpha-" + Guid.NewGuid().ToString("N"));
    private OpenTapHome Home()
    {
        var root = Path.Combine(_root, "home");
        Directory.CreateDirectory(root);
        using var stream = PublishedInstrumentComponents.OpenArchive();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(root);
        return new(root);
    }

    [Fact]
    public void Published_alpha_catalog_includes_every_vendor_and_shared_multifunction_capabilities_without_IO()
    {
        Assert.Equal("0.3.0-alpha.1", PublishedInstrumentComponents.Version);
        var home = Home();
        var adapters = AuthoringInstrumentCatalog.Discover(home);
        Assert.True(adapters.Count > 0, AuthoringInstrumentCatalog.LibraryReadiness(home).Reason);
        Assert.Contains(adapters, adapter => adapter.TypeId == DeviceDriverCatalog.RegisteredTypeId);
        Assert.Equal(56, DeviceDriverCatalog.All.Count);
        Assert.Equal(5, DeviceDriverCatalog.All.Select(info => info.Package).Distinct().Count());
        foreach (var info in DeviceDriverCatalog.All)
        {
            Assert.NotEmpty(info.Operations);
            Assert.Equal(info.Operations.Count, info.Operations.Distinct().Count());
            foreach (var name in info.Operations)
            {
                Assert.True(Enum.TryParse<CatalogOperation>(name, out _));
                Assert.True(AuthoringFunctionCatalog.TryGet("Device." + name, out _));
            }
        }
        var multifunction = DeviceDriverCatalog.Find("keithley.2602b");
        Assert.Contains("DmmMeasureVoltageDc", multifunction.Operations);
        Assert.Contains("DcPowerSupplyReadVoltage", multifunction.Operations);
    }

    [Theory]
    [InlineData("keysight.34461a", "DmmMeasureVoltageDc", "range=10", ":MEAS:VOLT:DC? 1e1", "1.25", Unit.Volt, 1.25)]
    [InlineData("keithley.2602b", "DcPowerSupplyReadVoltage", "channel=1", ":MEAS:VOLT? 1", "2.5", Unit.Volt, 2.5)]
    [InlineData("rigol.ds1054z", "OscilloscopeMeasureVpp", "channel=1", ":MEASure:VPP? CHAN1", "3.3", Unit.Volt, 3.3)]
    [InlineData("siglent.sdg2042x", "FunctionGeneratorReadFrequency", "", ":SOUR:FREQ?", "1000", Unit.Hertz, 1000)]
    [InlineData("tektronix.fca3100", "CounterMeasureFrequency", "", ":MEASure:FREQuency?", "1000000", Unit.Hertz, 1000000)]
    public void Public_vendor_transcripts_preserve_arguments_values_units_and_single_close(string id, string operation, string args, string command, string reply, Unit unit, double expected)
    {
        var connector = new StrictConnector((command, reply));
        using (var handle = DeviceDriverCatalog.Open(new(id, "MOCK::" + id), connector))
        {
            var value = handle.Invoke(DeviceOperationRequests.Create(operation, args)).IntoQuantity();
            Assert.Equal(expected, value.Value);
            Assert.Equal(unit, value.Unit);
            Assert.Equal(1, connector.Opens);
        }
        connector.AssertComplete();
    }

    [Fact]
    public void Multifunction_views_share_one_session_and_unsupported_requests_fail_before_connection()
    {
        var connector = new StrictConnector((":MEAS:VOLT:DC?", "1.25"), (":MEAS:VOLT? 1", "2.5"));
        using (var handle = DeviceDriverCatalog.Open(new("keithley.2602b", "MOCK::SMU"), connector))
        {
            Assert.Equal(1.25, handle.Invoke(new DmmMeasureVoltageDcRequest(null)).IntoQuantity().Value);
            Assert.Equal(2.5, handle.Invoke(new DcPowerSupplyReadVoltageRequest(1)).IntoQuantity().Value);
        }
        connector.AssertComplete();
        var unopened = new StrictConnector();
        Assert.Throws<DriverException>(() => DeviceDriverCatalog.Open(new("keysight.34461a", "MOCK::DMM"), unopened,
            new HashSet<Operation> { Operation.CatalogDcPowerSupplyReadVoltage }));
        Assert.Equal(0, unopened.Opens);
        Assert.Throws<ArgumentException>(() => DeviceDriverCatalog.Open(new("keysight.34401a", "GPIB0::22::INSTR"), unopened));
        Assert.Equal(0, unopened.Opens);
    }

    [Fact]
    public void Commissioned_34401A_uses_released_request_and_no_implicit_identity_reset_or_shutdown()
    {
        var connector = new StrictConnector((":MEAS:VOLT:DC? DEF,DEF", "+1.25000000E+00"));
        using (var handle = DeviceDriverCatalog.Open(new("keysight.34401a", "GPIB0::22::INSTR", Commissioning: "GpibScpiConfirmed"), connector))
        {
            Assert.Equal(1.25, handle.Invoke(new DmmMeasureVoltageDcRequest(null)).IntoQuantity().Value);
            Assert.DoesNotContain(CatalogOperation.QueryIdentity.Runtime(), handle.Operations);
            Assert.DoesNotContain(Operation.Shutdown, handle.Operations);
        }
        connector.AssertComplete();
    }

    [Fact]
    public void Mixed_vendor_authoring_roundtrip_preserves_resources_settings_and_operation_identity()
    {
        var home = Home();
        Assert.NotEmpty(AuthoringInstrumentCatalog.Discover(home));
        var slots = new[] { "keysight.34461a", "keithley.2602b", "rigol.ds1054z", "siglent.sdg2042x", "tektronix.fca3100" }
            .Select((id, i) => new InstrumentRef("Slot " + i, DeviceDriverCatalog.RegisteredTypeId, "MOCK::" + i)
            { Settings = new Dictionary<string, string> { ["DriverId"] = id, ["IoTimeoutMilliseconds"] = "8123" } }).ToArray();
        var result = new AuthoringPlanInitializer().Construct(new("mixed-alpha") { Home = home, Instruments = slots, IncludeSafeShutdown = false });
        var draft = result.Draft;
        var operations = new[] { "DmmMeasureVoltageDc", "DcPowerSupplyReadVoltage", "OscilloscopeMeasureVpp", "FunctionGeneratorReadFrequency", "CounterMeasureFrequency" };
        for (var i = 0; i < slots.Length; i++) draft = AuthoringRecipeCatalog.Apply(draft, "device:" + operations[i], slots[i].SlotName);
        var compiler = new PlanCompiler(selectedHome: home);
        var path = Path.Combine(_root, "mixed-alpha.TapPlan");
        compiler.Save(draft, path);
        var reopened = compiler.Load(path);
        Assert.Equal(slots.Length, reopened.Instruments.Count);
        foreach (var slot in slots)
        {
            var restored = reopened.Instruments.Single(item => item.SlotName == slot.SlotName);
            Assert.Equal(slot.TypeId, restored.TypeId);
            Assert.Equal(slot.VisaAddress, restored.VisaAddress);
            Assert.Equal(slot.Settings["DriverId"], restored.Settings["DriverId"]);
            Assert.Equal("8123", restored.Settings["IoTimeoutMilliseconds"]);
        }
        var metrics = AuthoringRecipeCatalog.EnumerateMetrics(reopened.Measure).ToArray();
        Assert.Equal(5, metrics.Length);
        Assert.Equal(operations.Select(name => "Device." + name), metrics.Select(metric => ((MeasureSource)metric.Source).FunctionId));
        Assert.Equal(5, InstrumentSlotCollector.FromPlan(TestPlan.Load(path)).Count);
    }

    [Fact]
    public void Arguments_reject_duplicates_unknown_names_and_nonfinite_values()
    {
        Assert.Throws<ArgumentException>(() => DeviceOperationRequests.Create("DmmMeasureVoltageDc", "range=1;Range=2"));
        Assert.Throws<ArgumentException>(() => DeviceOperationRequests.Create("DmmMeasureVoltageDc", "unexpected=1"));
        Assert.Throws<DriverException>(() => DeviceOperationRequests.Create("DmmMeasureVoltageDc", "range=NaN"));
        Assert.Null(((DmmMeasureVoltageDcRequest)DeviceOperationRequests.Create("DmmMeasureVoltageDc", "")).Range);
    }

    private sealed class StrictConnector(params (string Command, string Reply)[] script) : IScpiConnector
    {
        private readonly Queue<(string Command, string Reply)> _script = new(script);
        public int Opens { get; private set; }
        public int Closes { get; private set; }
        public IMessageIo Open(DriverOpenContext context) { Opens++; return new Message(this); }
        public void AssertComplete() { Assert.Empty(_script); Assert.Equal(1, Opens); Assert.Equal(1, Closes); }
        private sealed class Message(StrictConnector owner) : IMessageIo
        {
            public void Write(string message) => throw new InvalidOperationException("Unexpected write: " + message);
            public string Query(string message)
            {
                Assert.NotEmpty(owner._script);
                var expected = owner._script.Dequeue();
                Assert.Equal(expected.Command, message);
                return expected.Reply;
            }
            public void Dispose() => owner.Closes++;
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
