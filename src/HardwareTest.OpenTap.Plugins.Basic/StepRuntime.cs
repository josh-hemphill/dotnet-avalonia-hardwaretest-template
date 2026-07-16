namespace HardwareTest.OpenTap.Plugins.Basic;

/// Host assigns callbacks during plan execution so steps can honor pause / operator prompts.
public static class StepRuntime
{
    public static Action? WaitIfPaused { get; set; }

    /// Invoked by operator-prompt steps so the host can Pause and surface a Continue banner.
    public static Action<string>? RequestOperatorAttention { get; set; }
}
