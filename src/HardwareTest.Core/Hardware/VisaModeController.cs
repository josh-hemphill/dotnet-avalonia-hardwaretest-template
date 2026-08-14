using HardwareTest.Core.Engine;

namespace HardwareTest.Core.Hardware;

/// Controls the effective VISA mock/real mode and enables in-process swapping of factories.
/// Registered as the singleton for IVisaModeController, IVisaSessionFactory, IVisaResourceDiscovery, and IVisaBroker.
public interface IVisaModeController
{
    /// The mode the active factories are actually using (may differ from AppSettings during a refused flip).
    bool EffectiveUseMockVisa { get; }

    /// Raised (synchronously) after a successful in-process mode swap.
    event EventHandler? ModeApplied;

    /// Tries to swap mock↔real factories in-process.
    /// Returns true when the mode is now applied; false when the swap was refused.
    /// <paramref name="statusMessage"/> always carries a human-readable explanation suitable for the Status bar.
    bool TryApply(bool wantMock, out string statusMessage);
}

/// Builds the active factory/discovery pair for a mock-or-real mode.
/// Tests inject stubs for the real branch so CI never loads the IVI native runtime.
public delegate (IVisaSessionFactory Factory, IVisaResourceDiscovery Discovery) VisaModeInnerBuilder(
    bool useMock,
    VisaSessionGate gate,
    Action<string>? onIviError);

/// Delegating façade that forwards IVisaSessionFactory / IVisaResourceDiscovery calls to swappable
/// inners. Swaps are allowed only when no run is active and no broker sessions are open.
public sealed class VisaModeController : IVisaModeController, IVisaSessionFactory, IVisaResourceDiscovery, IVisaBroker
{
    private readonly VisaSessionGate _gate;
    private readonly IRunControl _runControl;
    private readonly IBenchOperationCoordinator? _bench;
    private readonly Action<string>? _onIviError;
    private readonly VisaModeInnerBuilder _buildInners;
    private readonly object _sync = new();
    private int _openSessions;

    private IVisaSessionFactory _factory;
    private IVisaResourceDiscovery _discovery;

    public VisaModeController(
        bool initialUseMockVisa,
        VisaSessionGate gate,
        IRunControl runControl,
        Action<string>? onIviError = null,
        VisaModeInnerBuilder? buildInners = null,
        IBenchOperationCoordinator? bench = null)
    {
        _gate = gate;
        _runControl = runControl;
        _bench = bench;
        _onIviError = onIviError;
        _buildInners = buildInners ?? DefaultBuildInners;
        EffectiveUseMockVisa = initialUseMockVisa;
        (_factory, _discovery) = _buildInners(initialUseMockVisa, gate, onIviError);
    }

    /// <inheritdoc/>
    public bool EffectiveUseMockVisa { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? ModeApplied;

    /// True when at least one broker session has not been disposed.
    public bool HasOpenSessions => Volatile.Read(ref _openSessions) > 0;

    /// <inheritdoc/>
    public bool TryApply(bool wantMock, out string statusMessage)
    {
        IDisposable? lease = null;
        if (_bench is not null && !_bench.TryEnter(BenchOperation.ModeSwap, out lease, out statusMessage))
        {
            return false;
        }

        using (lease)
        {
            return TryApplyCore(wantMock, out statusMessage);
        }
    }

    private bool TryApplyCore(bool wantMock, out string statusMessage)
    {
        if (wantMock == EffectiveUseMockVisa)
        {
            statusMessage = wantMock
                ? "Mock VISA already effective."
                : "Real VISA already effective.";
            return true;
        }

        if (_runControl.IsRunning)
        {
            statusMessage =
                "Cannot switch VISA mode while a run is active. " +
                "Finish or safety-stop the run, then save again.";
            return false;
        }

        if (HasOpenSessions || _gate.IsBusy)
        {
            statusMessage =
                "Cannot switch VISA mode while VISA sessions are open. " +
                "Close all sessions, then save again.";
            return false;
        }

        var (newFactory, newDiscovery) = _buildInners(wantMock, _gate, _onIviError);
        lock (_sync)
        {
            _factory = newFactory;
            _discovery = newDiscovery;
            EffectiveUseMockVisa = wantMock;
        }

        statusMessage = wantMock
            ? "Mock VISA applied — Instruments will reflect mock resources."
            : "Real VISA applied — Instruments will discover hardware resources.";

        ModeApplied?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// Forwards to the active inner factory (thread-safe snapshot of current inner).
    public async Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        IVisaSessionFactory factory;
        lock (_sync)
        {
            factory = _factory;
        }

        var inner = await factory.OpenAsync(resourceName, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _openSessions);
        return new TrackedVisaSession(inner, this);
    }

    private void ReleaseSession() => Interlocked.Decrement(ref _openSessions);

    /// Forwards to the active inner discovery (thread-safe snapshot of current inner).
    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        IVisaResourceDiscovery discovery;
        lock (_sync)
        {
            discovery = _discovery;
        }

        return discovery.FindAsync(cancellationToken);
    }

    private static (IVisaSessionFactory Factory, IVisaResourceDiscovery Discovery) DefaultBuildInners(
        bool useMock,
        VisaSessionGate gate,
        Action<string>? onIviError)
    {
        if (useMock)
        {
            return (new MockVisaSessionFactory(gate), new MockVisaResourceDiscovery());
        }

        return (new IviVisaSessionFactory(gate), new IviVisaResourceDiscovery(onIviError));
    }

    private sealed class TrackedVisaSession(IVisaSession inner, VisaModeController owner) : IVisaSession
    {
        private int _released;

        public string ResourceName => inner.ResourceName;

        public int IoTimeoutMilliseconds
        {
            get => inner.IoTimeoutMilliseconds;
            set => inner.IoTimeoutMilliseconds = value;
        }

        public Task WriteAsync(string command, CancellationToken cancellationToken = default)
            => inner.WriteAsync(command, cancellationToken);

        public Task<string> QueryAsync(string command, CancellationToken cancellationToken = default)
            => inner.QueryAsync(command, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    owner.ReleaseSession();
                }
            }
        }
    }
}
