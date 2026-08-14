using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// OpenTAP step that receives a per-run <see cref="IStepRuntime"/> from the host.
public interface IRuntimeAwareStep
{
    IStepRuntime? Runtime { get; }

    void AttachRuntime(IStepRuntime runtime);
}

/// Base for HardwareTest steps that honor pause and operator interaction.
public abstract class RuntimeAwareTestStep : TestStep, IRuntimeAwareStep
{
    private IStepRuntime? _runtime;

    IStepRuntime? IRuntimeAwareStep.Runtime => _runtime;

    public void AttachRuntime(IStepRuntime runtime)
        => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    protected void WaitIfPaused() => _runtime?.WaitIfPaused();

    protected OperatorInteractionResponse RequestInteraction(OperatorInteractionRequest request)
        => _runtime?.RequestInteraction(request)
           ?? OperatorInteractionResponse.Cancel(request.Id);

    protected void RequestOperatorAttention(string message)
        => _runtime?.RequestOperatorAttention(message);
}
