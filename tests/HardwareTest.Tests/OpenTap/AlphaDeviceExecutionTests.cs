using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Instruments;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Xunit;
using static HardwareTest.Tests.OpenTap.AlphaDevicePlanFixture;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class AlphaDeviceExecutionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commissioned_DMM_overload_is_not_a_zero_sample_and_true_zero_remains_valid_in_host_worker_records_and_reports(bool overload)
    {
        using var root = new TempDataDirectory();
        var path = SaveOverloadPlan(root.Path, overload);
        var broker = new QualifiedDmmBroker(overload);
        var settings = new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = root.Path, ExportOpenTapResults = true, EmbedPlotsInReport = true };
        var host = new OpenTapSession(settings, visaBroker: broker);
        await host.LoadPlanAsync(path);
        var result = await host.RunAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(overload ? RunResult.Failed : RunResult.Passed, result.Result);
        Assert.Equal(1, broker.Opens); Assert.Equal(1, broker.Queries); Assert.Equal(1, broker.Closes);
        var step = Assert.Single(result.Steps);
        if (overload)
        {
            Assert.Empty(result.Samples);
            Assert.Contains("Overload: no valid measurement (V)", step.OutcomeDetail);
            Assert.Contains("keysight.34401a [MOCK::AGILENT-OVERLOAD]", step.OutcomeDetail);
            Assert.Contains("Overload", Assert.Single(host.StepTree).KeyValue);
            var csv = Assert.Single(Directory.GetFiles(Path.Combine(root.Path, "runs", result.RunId, "opentap-results"), "*.csv"));
            Assert.Contains("MeasurementOutcome", Path.GetFileName(csv));
            Assert.Contains("Overload,V,MOCK::AGILENT-OVERLOAD", File.ReadAllText(csv));
        }
        else
        {
            var sample = Assert.Single(result.Samples); Assert.Equal(0, sample.Value); Assert.Equal("V", sample.Unit);
            Assert.Null(step.OutcomeDetail);
        }
        var store = new FileRunStore(root.RunsDirectory);
        var record = new TestRunRecord { RunId = result.RunId, PlanId = "qualified-dmm", PlanName = "Qualified DMM (simulated)", StartedAt = result.StartedAt, CompletedAt = result.CompletedAt, Result = result.Result, Samples = [.. result.Samples], Steps = [.. result.Steps] };
        await store.SaveAsync(record);
        var saved = (await store.LoadAsync(record.RunId))!;
        Assert.Equal(overload ? 0 : 1, saved.Samples.Count);
        Assert.Equal(step.OutcomeDetail, Assert.Single(saved.Steps).OutcomeDetail);
        using var reports = new Core.Reporting.TypstReportService(store, settings);
        var reportFiles = await reports.GenerateReportsAsync(saved, [ReportKinds.Status, ReportKinds.Certification]);
        Assert.Equal(2, reportFiles.Count);
        foreach (var report in reportFiles)
        {
            var text = ReadPdfText(report.PdfPath);
            if (overload) Assert.Contains("Overload: no valid measurement", text);
            else Assert.DoesNotContain("Overload", text);
        }
        using var worker = new OpenTapWorkerClient(settings);
        await worker.LoadPlanAsync(path);
        var remote = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(result.Result, remote.Result);
        Assert.Equal(overload ? 0 : 1, remote.Samples.Count);
        Assert.Equal(step.OutcomeDetail, Assert.Single(remote.Steps).OutcomeDetail);
        if (overload)
        {
            await worker.LoadPlanAsync(SaveOverloadPlan(root.Path, false));
            var next = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(RunResult.Passed, next.Result);
            Assert.Equal(0, Assert.Single(next.Samples).Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pause_finishes_current_reply_gates_next_device_operation_and_resume_or_stop_releases_owned_resource(bool stop)
    {
        using var root = new TempDataDirectory();
        var broker = new HeldReplyBroker();
        var session = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        var path = SavePausePlan(root.Path);
        await session.LoadPlanAsync(path);
        var run = session.RunAsync();
        await broker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        session.Pause();
        _ = broker.Release.TrySetResult();
        await broker.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Assert.False(broker.Following.Task.IsCompleted);
        Assert.False(run.IsCompleted);
        if (stop) session.Abort(); else session.Resume();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(stop ? RunResult.Cancelled : RunResult.Passed, result.Result);
        Assert.Equal(stop ? 1 : 2, broker.Queries);
        Assert.Equal(stop ? "Cancelled" : "Pass", result.Steps.Single(step => step.StepType.StartsWith("Following current", StringComparison.Ordinal)).OutcomeLabel);
        Assert.Equal(stop ? "Cancelled" : "Pass", session.StepTree.Single(step => step.Name == "Following current").Verdict);
        if (!stop)
        {
            Assert.Equal(new[] { 1.25, 0.005 }, result.Samples.Select(sample => sample.Value));
            Assert.Equal(new[] { "V", "A" }, result.Samples.Select(sample => sample.Unit));
        }
        Assert.Equal(1, broker.Opens); Assert.Equal(1, broker.Closes);
        broker.Arm(); _ = broker.Release.TrySetResult();
        var next = await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(RunResult.Passed, next.Result);
        Assert.Equal(2, next.Samples.Count);
        Assert.Equal(2, broker.Opens); Assert.Equal(2, broker.Closes);
    }

    [Fact]
    public async Task Saved_mixed_vendor_plan_executes_through_broker_and_preserves_five_values_units_and_resource_ownership()
    {
        using var root = new TempDataDirectory();
        var broker = new StrictBroker();
        var session = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        var path = SavePlan(root.Path);
        await session.LoadPlanAsync(path);
        Assert.Equal(5, session.InstrumentSlots.Count);
        Assert.All(session.InstrumentSlots, slot => Assert.True(slot.SupportsIdentityQuery));
        Assert.Contains(session.InstrumentSlots, slot => slot.TypeName.Contains("Keithley", StringComparison.Ordinal));
        Assert.Equal(0, broker.Opens);
        var summary = await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(summary.Result == RunResult.Passed, summary.ErrorMessage);
        Assert.Equal(5, summary.Samples.Count);
        foreach (var (Driver, Operation, Arguments, Query, Reply, Unit) in Cases)
        {
            var value = Assert.Single(summary.Samples, sample => sample.Channel == Driver);
            Assert.Equal(double.Parse(Reply, System.Globalization.CultureInfo.InvariantCulture), value.Value);
            Assert.Equal(Unit, value.Unit);
            var leaf = session.StepTree.SelectMany(node => node.Children).Single(node => node.Name == Driver + " measurement");
            Assert.Equal(leaf.Path, value.StepPath);
            Assert.Contains(Driver + " [scalar]", leaf.KeyValue);
            Assert.Equal("Pass", leaf.Verdict);
        }
        Assert.Equal(5, broker.Opens);
        Assert.Equal(5, broker.Closes);
        Assert.Equal(5, broker.Queries);
    }

    [Fact]
    public async Task Production_worker_runs_all_five_vendors_and_can_run_again_after_completion()
    {
        using var root = new TempDataDirectory();
        var path = SavePlan(root.Path);
        using var worker = new OpenTapWorkerClient(new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = root.Path });
        await worker.LoadPlanAsync(path);
        Assert.All(worker.InstrumentSlots, slot => Assert.True(slot.SupportsIdentityQuery));
        var first = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(first.Result == RunResult.Passed, first.ErrorMessage);
        Assert.Equal(5, first.Samples.Count);
        Assert.Equal(3, first.Samples.Count(sample => sample.Unit == "V"));
        Assert.Equal(2, first.Samples.Count(sample => sample.Unit == "Hz"));
        var second = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(second.Result == RunResult.Passed, second.ErrorMessage);
        Assert.Equal(5, second.Samples.Count);
    }

    [Fact]
    public async Task Unsupported_operation_is_reported_before_broker_allocation_and_next_valid_run_recovers()
    {
        using var root = new TempDataDirectory();
        var broker = new StrictBroker();
        var session = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        var path = SavePlan(root.Path);
        var text = File.ReadAllText(path).Replace("<Operation>DmmMeasureVoltageDc</Operation>", "<Operation>RfReceiverMeasurePower</Operation>", StringComparison.Ordinal);
        File.WriteAllText(path, text);
        await session.LoadPlanAsync(path);
        var failed = await session.RunAsync();
        Assert.Equal(RunResult.Error, failed.Result);
        Assert.Contains("does not advertise", failed.ErrorMessage);
        Assert.Equal(0, broker.Opens);
        await session.LoadPlanAsync(SavePlan(root.Path));
        Assert.Equal(RunResult.Passed, (await session.RunAsync()).Result);
        Assert.Equal(5, broker.Closes);
    }

    [Fact]
    public async Task Saved_multifunction_steps_share_one_device_in_host_and_production_worker()
    {
        using var root = new TempDataDirectory();
        var device = new RegisteredInstrument { Name = "Shared Keithley SMU", DriverId = "keithley.2602b", VisaAddress = "MOCK::SMU" };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "SMU DMM view", Instrument = device, Operation = "DmmMeasureVoltageDc", Channel = "smu.dmm" });
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "SMU supply view", Instrument = device, Operation = "DcPowerSupplyReadVoltage", Arguments = "channel=1", Channel = "smu.psu" });
        var path = Path.Combine(root.Path, "multifunction.TapPlan"); plan.Save(path);
        _ = Assert.Single(InstrumentSlotCollector.FromPlan(TestPlan.Load(path)));
        var broker = new TranscriptBroker();
        var host = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        await host.LoadPlanAsync(path);
        _ = Assert.Single(host.InstrumentSlots);
        var result = await host.RunAsync();
        Assert.Equal(RunResult.Passed, result.Result);
        Assert.Equal(new[] { 1.25, 2.5 }, result.Samples.Select(sample => sample.Value));
        Assert.Equal(1, broker.Closes);
        using var worker = new OpenTapWorkerClient(new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = root.Path });
        await worker.LoadPlanAsync(path);
        _ = Assert.Single(worker.InstrumentSlots);
        var remote = await worker.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(RunResult.Passed, remote.Result);
        Assert.Equal(new[] { "smu.dmm", "smu.psu" }, remote.Samples.Select(sample => sample.Channel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_trace_and_configured_output_shutdown_reach_the_normal_result_and_broker_contracts(bool failShutdown)
    {
        using var root = new TempDataDirectory();
        var broker = new TranscriptBroker { FailShutdown = failShutdown };
        var session = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        var scope = new RegisteredInstrument { Name = "Rigol scope", DriverId = "rigol.ds1054z", VisaAddress = "MOCK::SCOPE" };
        var generator = new RegisteredInstrument { Name = "Siglent generator", DriverId = "siglent.sdg2042x", VisaAddress = "MOCK::GEN", Outputs = "fgen" };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "Explicit identity", Instrument = scope, Operation = "QueryIdentity" });
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "Voltage trace", Instrument = scope, Operation = "OscilloscopeCaptureVoltageTrace", Arguments = "channel=1", Channel = "rigol.trace" });
        plan.ChildTestSteps.Add(new DeviceOperationStep { Name = "Configured output shutdown", Instrument = generator, Operation = "Shutdown" });
        var path = Path.Combine(root.Path, "trace.TapPlan"); plan.Save(path);
        await session.LoadPlanAsync(path);
        var result = await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(result.Result == (failShutdown ? RunResult.Error : RunResult.Passed), result.ErrorMessage);
        Assert.Equal(3, result.Samples.Count);
        Assert.Equal(new[] { 1.25, 2.5, 3.75 }, result.Samples.Select(sample => sample.Value));
        Assert.Equal(new double?[] { 0, 1, 2 }, result.Samples.Select(sample => sample.ElapsedMs));
        Assert.All(result.Samples, sample => { Assert.Equal("V", sample.Unit); Assert.Equal("rigol.trace", sample.Channel); Assert.Equal("Voltage trace", sample.StepPath); });
        Assert.Contains("RIGOL,DS1054Z,TRACE-001,00.04", session.StepTree.Single(step => step.Name == "Explicit identity").KeyValue);
        Assert.Equal(2, broker.Closes);
        Assert.Equal(new[] { ":WAVeform:SOURce CHAN1", ":WAVeform:FORMat ASCii", ":OUTP OFF" }, broker.Writes);
    }

    private sealed class TranscriptBroker : IVisaBroker
    {
        public int Closes;
        public bool FailShutdown;
        public List<string> Writes = [];
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
            => Task.FromResult<IVisaSession>(new Lease(this, resourceName));
        private sealed class Lease(TranscriptBroker owner, string resource) : IVisaSession
        {
            public string ResourceName => resource;
            public int IoTimeoutMilliseconds { get; set; }
            public Task WriteAsync(string command, CancellationToken cancellationToken = default) { owner.Writes.Add(command); if (owner.FailShutdown && command == ":OUTP OFF") throw new IOException("Simulated output shutdown failure"); return Task.CompletedTask; }
            public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult(command switch
            {
                ":MEAS:VOLT:DC?" => "1.25",
                ":MEAS:VOLT? 1" => "2.5",
                "*IDN?" => "RIGOL,DS1054Z,TRACE-001,00.04",
                ":WAVeform:PREamble?" => "0,0,3,1,0.001,0,0,1,0,0",
                ":WAVeform:DATA?" => "1.25,2.5,3.75",
                _ => throw new InvalidOperationException("Unexpected query " + command)
            });
            public ValueTask DisposeAsync() { owner.Closes++; return ValueTask.CompletedTask; }
        }
    }

    [Theory]
    [InlineData("open")]
    [InlineData("query")]
    [InlineData("parse")]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("cancel-close")]
    [InlineData("close")]
    public async Task Device_faults_and_stop_close_every_owned_lease_and_allow_a_clean_next_run(string fault)
    {
        using var root = new TempDataDirectory();
        var broker = new FaultBroker { Fault = fault };
        var session = new OpenTapSession(new() { UseMockVisa = true, DataDirectory = root.Path }, visaBroker: broker);
        var path = SavePlan(root.Path);
        if (fault == "timeout") File.WriteAllText(path, File.ReadAllText(path).Replace("<IoTimeoutMilliseconds>5000", "<IoTimeoutMilliseconds>100", StringComparison.Ordinal));
        await session.LoadPlanAsync(path);
        var running = session.RunAsync();
        if (fault.StartsWith("cancel", StringComparison.Ordinal))
        {
            await broker.QueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            session.Abort();
        }
        var failed = await running.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.NotEqual(RunResult.Passed, failed.Result);
        if (fault != "cancel")
        {
            Assert.Contains("Device I/O failed", failed.ErrorMessage);
            Assert.Contains("MOCK::", failed.ErrorMessage);
            Assert.Contains("before retrying", failed.ErrorMessage);
        }
        if (fault == "query") Assert.Contains("Simulated command failure", failed.ErrorMessage);
        if (fault is "query" or "parse") Assert.Contains(failed.Steps, step => step.OutcomeLabel == "Error");
        if (fault == "timeout") Assert.Contains("timed out", failed.ErrorMessage);
        if (fault.StartsWith("cancel", StringComparison.Ordinal)) Assert.Equal(RunResult.Cancelled, failed.Result);
        Assert.Equal(broker.Opens, broker.Closes);
        broker.Fault = "";
        await session.LoadPlanAsync(SavePlan(root.Path));
        var recovered = await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(recovered.Result == RunResult.Passed, recovered.ErrorMessage);
        Assert.Equal(5, recovered.Samples.Count);
        Assert.Equal(broker.Opens, broker.Closes);
    }

    private sealed class FaultBroker : IVisaBroker
    {
        public string Fault = "";
        public int Opens;
        public int Closes;
        public TaskCompletionSource QueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            if (Fault == "open" && resourceName.Contains("rigol", StringComparison.Ordinal)) throw new IOException("Simulated missing Rigol device");
            _ = Interlocked.Increment(ref Opens);
            return Task.FromResult<IVisaSession>(new Lease(this, resourceName));
        }
        private sealed class Lease(FaultBroker owner, string resource) : IVisaSession
        {
            private int _disposed;
            public string ResourceName => resource;
            public int IoTimeoutMilliseconds { get; set; }
            public Task WriteAsync(string command, CancellationToken cancellationToken = default) => throw new IOException("Simulated write failure");
            public async Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
            {
                _ = owner.QueryEntered.TrySetResult();
                if (owner.Fault is "cancel" or "cancel-close" or "timeout") await Task.Delay(Timeout.Infinite, cancellationToken);
                if (owner.Fault == "query") throw new IOException("Simulated command failure");
                if (owner.Fault == "parse") return "not-a-number";
                var (Driver, Operation, Arguments, Query, Reply, Unit) = Cases.Single(item => resource == "MOCK::" + item.Driver);
                Assert.Equal(Query, command);
                return Reply;
            }
            public ValueTask DisposeAsync()
            {
                Assert.Equal(0, Interlocked.Exchange(ref _disposed, 1));
                _ = Interlocked.Increment(ref owner.Closes);
                if (owner.Fault is "close" or "cancel-close") throw new IOException("Simulated transport close failure");
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StrictBroker : IVisaBroker
    {
        public int Opens;
        public int Closes;
        public int Queries;
        public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref Opens);
            var (Driver, Operation, Arguments, Query, Reply, Unit) = Cases.Single(item => resourceName == "MOCK::" + item.Driver);
            return Task.FromResult<IVisaSession>(new Lease(this, resourceName, Query, Reply));
        }
        private sealed class Lease(StrictBroker owner, string resource, string query, string reply) : IVisaSession
        {
            private int _disposed;
            public string ResourceName => resource;
            public int IoTimeoutMilliseconds { get; set; }
            public Task WriteAsync(string command, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected write: " + command);
            public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
            {
                Assert.Equal(query, command);
                _ = Interlocked.Increment(ref owner.Queries);
                return Task.FromResult(reply);
            }
            public ValueTask DisposeAsync()
            {
                Assert.Equal(0, Interlocked.Exchange(ref _disposed, 1));
                _ = Interlocked.Increment(ref owner.Closes);
                return ValueTask.CompletedTask;
            }
        }
    }
}
