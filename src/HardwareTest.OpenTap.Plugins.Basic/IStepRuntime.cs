namespace HardwareTest.OpenTap.Plugins.Basic;

/// Per-run pause and operator interaction. Assigned onto steps before Execute — not process-global.
/// Called on the OpenTAP plan thread; must not touch Avalonia controls directly.
public interface IStepRuntime
{
    void WaitIfPaused();

    /// Blocks until the host returns a response (typically after Avalonia Continue).
    OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request);

    /// Confirm-only attention: builds a request with no fields and invokes RequestInteraction.
    void RequestOperatorAttention(string message)
        => RequestInteraction(OperatorInteractionRequest.ConfirmOnly(message));
}

/// Used when a step has no attached run context (idle / after detach).
public sealed class NoOpStepRuntime : IStepRuntime
{
    public static NoOpStepRuntime Instance { get; } = new();

    public void WaitIfPaused()
    {
    }

    public OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request)
        => OperatorInteractionResponse.Cancel(request.Id);
}
