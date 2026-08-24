namespace HardwareTest.Features.Shell;

/// Shared operator copy for Stop Run — one meaning across the Run board and nav footer.
public static class StopRunCopy
{
    public const string Label = "Stop Run";

    /// Cooperative software stop, then worker kill. Not a hardware interlock.
    public const string CooperativeTip =
        "Stop Run — cooperative software stop, then kill the OpenTAP worker if a step ignores cancel. Not a hardware interlock.";

    public const string CancelPromptTip =
        "Cancel prompt (aborts run) — operator interaction is cancelled via Stop Run. Not a hardware interlock.";

    public const string CancelShutdownTip = "Cancel the in-progress software stop";

    public const string InProgressTip = "Run in progress — use Stop Run to abort.";
}
