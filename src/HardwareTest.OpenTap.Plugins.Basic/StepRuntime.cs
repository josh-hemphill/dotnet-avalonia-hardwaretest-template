namespace HardwareTest.OpenTap.Plugins.Basic;

/// Host assigns callbacks during plan execution so steps can honor pause / operator prompts.
/// Callbacks run on the OpenTAP plan thread and must not touch Avalonia controls directly.
public static class StepRuntime
{
    public static Action? WaitIfPaused { get; set; }

    /// Blocks until the host returns a response (typically after Avalonia Continue).
    public static Func<OperatorInteractionRequest, OperatorInteractionResponse>? RequestInteraction { get; set; }

    /// Confirm-only attention: builds a request with no fields and invokes RequestInteraction.
    public static void RequestOperatorAttention(string message)
    {
        if (RequestInteraction is null)
        {
            return;
        }

        RequestInteraction(OperatorInteractionRequest.ConfirmOnly(message));
    }
}
