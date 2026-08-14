using Serilog;

namespace HardwareTest.Core.Engine;

/// Hardware interlock / output-disable seam. Avalonia-free and OpenTAP-free.
/// Default composition is a no-op until a bench adapter is registered.
public interface ISafetyController
{
    /// True only when a real bench adapter has an armed interlock. The no-op is never armed.
    bool IsArmed { get; }

    /// Operator-facing status. The no-op must not say "armed".
    string StatusText { get; }

    /// Optional named channels (ESTOP loop, output disable, …). Empty for the no-op.
    IReadOnlyList<string> Channels { get; }

    /// Drive outputs to a safe idle. Idempotent. The no-op logs and does not throw.
    void SafeIdle();
}

/// Logs that no interlock adapter is wired. Never reports itself as armed.
public sealed class NoOpSafetyController : ISafetyController
{
    public const string NotWiredStatus = "Not wired";

    public bool IsArmed => false;

    public string StatusText => NotWiredStatus;

    public IReadOnlyList<string> Channels { get; } = [];

    public void SafeIdle()
        => Log.Information("SafeIdle: no hardware interlock adapter is wired.");
}
