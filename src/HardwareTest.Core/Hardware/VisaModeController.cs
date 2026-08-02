using HardwareTest.Core.Engine;

namespace HardwareTest.Core.Hardware;

/// Controls the effective VISA mock/real mode and enables in-process swapping of factories.
/// Registered as the singleton for IVisaModeController, IVisaSessionFactory, and IVisaResourceDiscovery.
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

/// Delegating façade that forwards IVisaSessionFactory / IVisaResourceDiscovery calls to swappable
/// inners. Swaps are allowed only when no run is active and the VISA gate holds no open sessions.
public sealed class VisaModeController : IVisaModeController, IVisaSessionFactory, IVisaResourceDiscovery
{
    private readonly VisaSessionGate _gate;
    private readonly IRunControl _runControl;
    private readonly Action<string>? _onIviError;
    private readonly object _sync = new();

    private IVisaSessionFactory _factory;
    private IVisaResourceDiscovery _discovery;

    public VisaModeController(
        bool initialUseMockVisa,
        VisaSessionGate gate,
        IRunControl runControl,
        Action<string>? onIviError = null)
    {
        _gate = gate;
        _runControl = runControl;
        _onIviError = onIviError;
        EffectiveUseMockVisa = initialUseMockVisa;
        (_factory, _discovery) = BuildInners(initialUseMockVisa);
    }

    /// <inheritdoc/>
    public bool EffectiveUseMockVisa { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? ModeApplied;

    /// <inheritdoc/>
    public bool TryApply(bool wantMock, out string statusMessage)
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

        if (_gate.IsBusy)
        {
            statusMessage =
                "Cannot switch VISA mode while VISA sessions are open. " +
                "Close all sessions, then save again.";
            return false;
        }

        var (newFactory, newDiscovery) = BuildInners(wantMock);
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
    public Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        IVisaSessionFactory factory;
        lock (_sync)
        {
            factory = _factory;
        }

        return factory.OpenAsync(resourceName, cancellationToken);
    }

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

    private (IVisaSessionFactory factory, IVisaResourceDiscovery discovery) BuildInners(bool useMock)
    {
        if (useMock)
        {
            return (new MockVisaSessionFactory(_gate), new MockVisaResourceDiscovery());
        }

        return (new IviVisaSessionFactory(_gate), new IviVisaResourceDiscovery(_onIviError));
    }
}
