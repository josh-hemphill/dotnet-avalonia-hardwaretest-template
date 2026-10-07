using System.Reflection;
using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.Tests.Fixtures;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class ExplicitSlotBindingTests
{
    [Fact]
    public async Task Actual_two_mock_instruments_bind_only_selected_exact_slot()
    {
        using var temporary = new TempDataDirectory();
        var path = SavePlan(temporary.Path);
        var session = new OpenTapSession();
        await session.LoadPlanAsync(path);
        await VerifyBindings(session);
    }

    [Fact]
    public async Task Binder_waiting_for_session_lock_refuses_after_run_claims_execution_gate()
    {
        using var temporary = new TempDataDirectory();
        var session = new OpenTapSession();
        await session.LoadPlanAsync(SavePlan(temporary.Path));
        var sync = typeof(OpenTapSession).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        using var gateClaimed = new ManualResetEventSlim();
        using var allowRun = new ManualResetEventSlim();
        using var binderFinished = new ManualResetEventSlim();
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(session.IsExecuting) && session.IsExecuting)
            {
                gateClaimed.Set();
                allowRun.Wait(TimeSpan.FromSeconds(10));
            }
        };
        bool? bound = null;
        var binder = new Thread(() =>
        {
            bound = session.TryBindSlotResource("DMM-B", "MOCK::forbidden");
            binderFinished.Set();
        })
        { IsBackground = true };
        Task<OpenTapRunSummary>? run = null;
        try
        {
            lock (sync)
            {
                binder.Start();
                Assert.True(SpinWait.SpinUntil(() => (binder.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
                run = Task.Run(() => session.RunAsync());
                Assert.True(gateClaimed.Wait(TimeSpan.FromSeconds(5)));
            }
            Assert.True(binderFinished.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(bound);
            Assert.True(session.IsExecuting);
            Assert.False(session.TryBindSlotResource("DMM-A", "MOCK::also-forbidden"));
            AssertResources(session, "MOCK::A-original", "MOCK::B-original");
        }
        finally
        {
            allowRun.Set();
            if (run is not null) await run.WaitAsync(TimeSpan.FromSeconds(60));
            binder.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Worker_preserves_exact_slot_binding_and_current_mapping_wire_contract()
    {
        using var temporary = new TempDataDirectory();
        var path = SavePlan(temporary.Path);
        using var session = new OpenTapWorkerClient(new AppSettings
        {
            UseMockVisa = true,
            CrashEnabled = false,
            DataDirectory = temporary.Path,
        });
        await session.LoadPlanAsync(path);
        await VerifyBindings(session);
        var request = new WorkerStationDutRequest { SlotToResource = new() { ["DMM-B"] = "MOCK::B" } };
        var payload = WorkerProtocol.SerializePayload(request, WorkerJsonContext.Default.WorkerStationDutRequest);
        Assert.Contains("slotToResource", payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("roleToResource", payload.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_opens_genuine_current_library_using_mock_broker_and_embedded_registry()
    {
        using var temporary = new TempDataDirectory();
        var assembly = PublishedLibraryFixture.Assembly;
        var instrument = PublishedLibraryFixture.CreateDmm();
        var plan = new TestPlan();
        foreach (var name in new[] { "IdentityQueryStep", "SafeShutdownStep" })
        {
            var step = (TestStep)Activator.CreateInstance(assembly.GetType("InstrumentComponents.OpenTap." + name, true)!)!;
            step.GetType().GetProperty("Instrument")!.SetValue(step, instrument);
            plan.ChildTestSteps.Add(step);
        }
        var path = Path.Combine(temporary.Path, "current-library.TapPlan");
        plan.Save(path);
        using var session = new OpenTapWorkerClient(new AppSettings { UseMockVisa = true, CrashEnabled = false, DataDirectory = temporary.Path });
        await session.LoadPlanAsync(path);
        var summary = await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(summary.Result == RunResult.Passed, summary.ErrorMessage ?? $"Worker result: {summary.Result}");
        using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HardwareTest.OpenTap.Worker.runtimeconfig.json")));
        Assert.True(runtime.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties")
            .GetProperty("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault").GetBoolean());
    }

    private static async Task VerifyBindings(IOpenTapSession session)
    {
        Assert.Equal(2, session.InstrumentSlots.Count);
        Assert.All(session.InstrumentSlots, slot => Assert.Equal("dmm", slot.RoleHint));
        Assert.True(session.TryBindSlotResource("dmm-b", " MOCK::B-selected "));
        AssertResources(session, "MOCK::A-original", "MOCK::B-selected");
        foreach (var invalid in new[] { ("unknown", "MOCK::wrong"), ("", "MOCK::wrong"), (" ", "MOCK::wrong"), ("DMM-A", ""), ("DMM-A", " ") })
        {
            Assert.False(session.TryBindSlotResource(invalid.Item1, invalid.Item2));
            AssertResources(session, "MOCK::A-original", "MOCK::B-selected");
        }

        await session.ApplyStationAndDutAsync(new StationProfile(new Dictionary<string, string>
        {
            ["dmm"] = "MOCK::broad-role",
            ["unknown"] = "MOCK::wrong",
            ["dmm-b"] = "MOCK::B-override",
        }), new DutIdentity("fixture"));
        AssertResources(session, "MOCK::A-original", "MOCK::B-override");
    }

    private static void AssertResources(IOpenTapSession session, string first, string second)
    {
        Assert.Equal(first, session.InstrumentSlots.Single(slot => slot.Name == "DMM-A").ResourceName);
        Assert.Equal(second, session.InstrumentSlots.Single(slot => slot.Name == "DMM-B").ResourceName);
        if (session is OpenTapSession realSession)
        {
            var plan = Assert.IsType<TestPlan>(typeof(OpenTapSession).GetField("_plan", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(realSession));
            var instruments = InstrumentResourceAccess.CollectFromPlan(plan).ToArray();
            Assert.Equal(first, InstrumentResourceAccess.GetResource(instruments.Single(instrument => instrument.Name == "DMM-A")));
            Assert.Equal(second, InstrumentResourceAccess.GetResource(instruments.Single(instrument => instrument.Name == "DMM-B")));
        }
    }

    private static string SavePlan(string directory)
    {
        OpenTapPluginSearch.SearchSerialized();
        var first = new MockDmmInstrument { Name = "DMM-A", ResourceName = "MOCK::A-original" };
        var second = new MockDmmInstrument { Name = "DMM-B", ResourceName = "MOCK::B-original" };
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(new AcquireVoltageStep { Name = "Acquire A", Instrument = first });
        plan.ChildTestSteps.Add(new AcquireVoltageStep { Name = "Acquire B", Instrument = second });
        var path = Path.Combine(directory, "explicit-slots.TapPlan");
        plan.Save(path);
        return path;
    }
}
