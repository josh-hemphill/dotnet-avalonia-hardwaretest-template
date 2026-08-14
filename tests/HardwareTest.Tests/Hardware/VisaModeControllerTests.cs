using HardwareTest.Core;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaModeControllerTests
{
    /// Builds mock/real inners without touching Ivi.Visa — constructing real IVI types without a
    /// vendor runtime leaves ConflictManager finalizers that throw DllNotFoundException (xUnit FATAL).
    private static VisaModeInnerBuilder TestInners { get; } = (useMock, gate, _) =>
        useMock
            ? (new MockVisaSessionFactory(gate), new MockVisaResourceDiscovery())
            : (new UnavailableVisaSessionFactory(), new UnavailableVisaResourceDiscovery());

    private static VisaModeController MakeController(
        bool initialMock,
        VisaSessionGate? gate = null,
        IRunControl? runControl = null,
        IBenchOperationCoordinator? bench = null)
    {
        gate ??= new VisaSessionGate();
        runControl ??= new StubRunControl(isRunning: false);
        return new VisaModeController(initialMock, gate, runControl, buildInners: TestInners, bench: bench);
    }

    // ── Initial state: mock ────────────────────────────────────────────────

    [Fact]
    public void Initial_effective_mock_matches_constructor()
    {
        var controller = MakeController(initialMock: true);
        Assert.True(controller.EffectiveUseMockVisa);
    }

    [Fact]
    public void Initial_effective_real_matches_constructor()
    {
        var controller = MakeController(initialMock: false);
        Assert.False(controller.EffectiveUseMockVisa);
    }

    [Fact]
    public async Task Initial_mock_factory_returns_mock_catalog()
    {
        var controller = MakeController(initialMock: true);
        var found = await ((IVisaResourceDiscovery)controller).FindAsync();
        Assert.Contains(found, r => r.Resource.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Initial_real_factory_throws_without_ivi_runtime()
    {
        var controller = MakeController(initialMock: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IVisaResourceDiscovery)controller).FindAsync());
    }

    // ── TryApply: already same mode ────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryApply_same_mode_returns_true_without_event(bool useMock)
    {
        var controller = MakeController(useMock);
        var eventFired = false;
        controller.ModeApplied += (_, _) => eventFired = true;

        var result = controller.TryApply(useMock, out var msg);

        Assert.True(result);
        Assert.False(eventFired);
        Assert.False(string.IsNullOrWhiteSpace(msg));
    }

    // ── TryApply: refused while running ────────────────────────────────────

    [Fact]
    public void TryApply_refused_while_run_is_active()
    {
        var stub = new StubRunControl(isRunning: true);
        var controller = MakeController(initialMock: true, runControl: stub);
        var eventFired = false;
        controller.ModeApplied += (_, _) => eventFired = true;

        var result = controller.TryApply(wantMock: false, out var msg);

        Assert.False(result);
        Assert.False(eventFired);
        Assert.True(controller.EffectiveUseMockVisa, "effective mode unchanged on refuse");
        Assert.Contains("run", msg, StringComparison.OrdinalIgnoreCase);
    }

    // ── TryApply: refused while gate busy ──────────────────────────────────

    [Fact]
    public async Task TryApply_refused_while_visa_gate_is_busy()
    {
        var gate = new VisaSessionGate();
        var controller = MakeController(initialMock: true, gate: gate);

        var semaphoreHeld = new TaskCompletionSource();
        var releaseSignal = new TaskCompletionSource();

        var gateTask = gate.RunAsync(
            async ct =>
            {
                semaphoreHeld.SetResult();
                await releaseSignal.Task.WaitAsync(ct);
            },
            CancellationToken.None);

        await semaphoreHeld.Task;
        Assert.True(gate.IsBusy);

        var result = controller.TryApply(wantMock: false, out var msg);

        Assert.False(result);
        Assert.True(controller.EffectiveUseMockVisa, "effective mode unchanged on refuse");
        Assert.Contains("session", msg, StringComparison.OrdinalIgnoreCase);

        releaseSignal.SetResult();
        await gateTask;
        Assert.False(gate.IsBusy);
    }

    // ── TryApply: successful swap mock → real ──────────────────────────────

    [Fact]
    public void TryApply_mock_to_real_succeeds_when_idle()
    {
        var controller = MakeController(initialMock: true);
        var eventFired = false;
        controller.ModeApplied += (_, _) => eventFired = true;

        var result = controller.TryApply(wantMock: false, out var msg);

        Assert.True(result);
        Assert.True(eventFired);
        Assert.False(controller.EffectiveUseMockVisa);
        Assert.False(string.IsNullOrWhiteSpace(msg));
    }

    // ── TryApply: successful swap real → mock ──────────────────────────────

    [Fact]
    public async Task TryApply_real_to_mock_succeeds_and_discovery_returns_mock_catalog()
    {
        var controller = MakeController(initialMock: false);

        var result = controller.TryApply(wantMock: true, out _);

        Assert.True(result);
        Assert.True(controller.EffectiveUseMockVisa);

        var found = await ((IVisaResourceDiscovery)controller).FindAsync();
        Assert.Contains(found, r => r.Resource.StartsWith("MOCK::", StringComparison.OrdinalIgnoreCase));
    }

    // ── IsBusy on VisaSessionGate ──────────────────────────────────────────

    [Fact]
    public async Task VisaSessionGate_IsBusy_reflects_held_gate()
    {
        var gate = new VisaSessionGate();
        Assert.False(gate.IsBusy);

        var semaphoreHeld = new TaskCompletionSource();
        var releaseSignal = new TaskCompletionSource();

        var gateTask = gate.RunAsync(
            async ct =>
            {
                semaphoreHeld.SetResult();
                await releaseSignal.Task.WaitAsync(ct);
            },
            CancellationToken.None);

        await semaphoreHeld.Task;
        Assert.True(gate.IsBusy);

        releaseSignal.SetResult();
        await gateTask;
        Assert.False(gate.IsBusy);
    }

    // ── TryApply: refused while a broker session is still open ─────────────

    [Fact]
    public async Task TryApply_refused_while_broker_session_is_open()
    {
        var controller = MakeController(initialMock: true);
        await using var session = await ((IVisaBroker)controller).OpenAsync("MOCK::INSTR0");
        Assert.True(controller.HasOpenSessions);

        var result = controller.TryApply(wantMock: false, out var msg);

        Assert.False(result);
        Assert.True(controller.EffectiveUseMockVisa, "effective mode unchanged on refuse");
        Assert.Contains("session", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryApply_succeeds_after_broker_session_is_disposed()
    {
        var controller = MakeController(initialMock: true);
        var session = await ((IVisaBroker)controller).OpenAsync("MOCK::INSTR0");
        await session.DisposeAsync();
        Assert.False(controller.HasOpenSessions);

        var result = controller.TryApply(wantMock: false, out _);

        Assert.True(result);
        Assert.False(controller.EffectiveUseMockVisa);
    }

    // ── TryApply: refused while the bench coordinator is held ──────────────

    [Fact]
    public void TryApply_refused_while_coordinator_holds_id_query()
    {
        var bench = new BenchOperationCoordinator();
        var controller = MakeController(initialMock: true, bench: bench);
        Assert.True(bench.TryEnter(BenchOperation.IdQuery, out var lease, out _));
        using (lease)
        {
            var result = controller.TryApply(wantMock: false, out var msg);
            Assert.False(result);
            Assert.True(controller.EffectiveUseMockVisa);
            Assert.Contains("Instruments query", msg, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TryApply_refused_while_coordinator_holds_run()
    {
        var bench = new BenchOperationCoordinator();
        var controller = MakeController(initialMock: true, bench: bench);
        Assert.True(bench.TryEnter(BenchOperation.Run, out var lease, out _));
        using (lease)
        {
            var result = controller.TryApply(wantMock: false, out var msg);
            Assert.False(result);
            Assert.Contains("run", msg, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── DI composition resolves IVisaModeController ────────────────────────

    [Fact]
    public async Task DI_resolves_IVisaModeController_and_is_same_instance_as_factories()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();
        using var temp = new TempDataDirectory();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync();
        store.AppSettings.UseMockVisa = true;

        var services = new ServiceCollection();
        services.AddHardwareTestCore(store);
        await using var sp = services.BuildServiceProvider();

        var controller = sp.GetRequiredService<IVisaModeController>();
        var factory = sp.GetRequiredService<IVisaSessionFactory>();
        var discovery = sp.GetRequiredService<IVisaResourceDiscovery>();
        var broker = sp.GetRequiredService<IVisaBroker>();

        Assert.NotNull(controller);
        Assert.Same(controller, factory);
        Assert.Same(controller, discovery);
        Assert.Same(controller, broker);
        Assert.True(controller.EffectiveUseMockVisa);
    }

    // ── Regression: factory and controller agree after successful swap ──────

    [Fact]
    public async Task Factory_serves_mock_after_swap_to_mock()
    {
        var controller = MakeController(initialMock: false);
        controller.TryApply(wantMock: true, out _);

        Assert.True(controller.EffectiveUseMockVisa);
        await using var session = await ((IVisaSessionFactory)controller).OpenAsync("MOCK::INSTR0");
        Assert.Contains("MOCK", session.ResourceName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Factory_throws_for_real_resource_when_real_and_no_ivi_runtime()
    {
        var controller = MakeController(initialMock: true);
        controller.TryApply(wantMock: false, out _);

        Assert.False(controller.EffectiveUseMockVisa);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IVisaSessionFactory)controller).OpenAsync("GPIB0::1::INSTR"));
    }

    [Fact]
    public async Task Broker_session_forwards_io_timeout()
    {
        var controller = MakeController(initialMock: true);
        await using var session = await ((IVisaBroker)controller).OpenAsync("MOCK::INSTR0");
        session.IoTimeoutMilliseconds = 25_000;
        Assert.Equal(25_000, session.IoTimeoutMilliseconds);
    }
}

/// Stand-in for real IVI discovery when no vendor VISA runtime is present (unit tests / CI).
file sealed class UnavailableVisaResourceDiscovery : IVisaResourceDiscovery
{
    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "VISA discovery failed: IVI runtime unavailable in this test host. Install a vendor VISA runtime or enable Use mock VISA.");
    }
}

/// Stand-in for real IVI session open when no vendor VISA runtime is present (unit tests / CI).
file sealed class UnavailableVisaSessionFactory : IVisaSessionFactory
{
    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            $"Failed to open VISA resource '{resourceName}'. Ensure a vendor VISA runtime is installed, or enable UseMockVisa.");
    }
}

/// Minimal IRunControl stub for VisaModeController tests.
file sealed class StubRunControl : IRunControl
{
    public StubRunControl(bool isRunning) => IsRunning = isRunning;

    public bool IsRunning { get; }
    public bool IsPaused => false;
    public bool IsSafetyStopping => false;
    public bool WasSafetyStopRequested => false;
    public CancellationToken SafetyShutdownToken => CancellationToken.None;

#pragma warning disable CS0067
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public void AttachRun(CancellationTokenSource runCts) { }
    public void DetachRun() { }
    public void Pause() { }
    public void Resume() { }
    public void RequestCancel() { }
    public void RequestSafetyStop() { }
    public void CancelSafetyShutdown() { }
    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
