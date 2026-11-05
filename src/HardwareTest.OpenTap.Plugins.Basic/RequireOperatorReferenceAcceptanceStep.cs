using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// A per-run capability supplied by the operator host after freezing acceptance inputs.
/// This is not an authentication interface and is never serialized into a TapPlan.
public interface IOperatorReferenceAcceptanceRuntime
{
    bool IsOperatorReferenceAcceptancePrepared { get; }
    bool IsReferenceAcquisition { get; }
}

/// Fail-closed guard for plans whose final reference acceptance belongs to the operator.
[Display("Check reference inputs", Groups: ["HardwareTest", "Setup"],
    Description: "Requires an operator host with a freshly prepared reference selection or explicit acquisition context. Standalone execution cannot establish reference acceptance.")]
public sealed class RequireOperatorReferenceAcceptanceStep : RuntimeAwareTestStep
{
    public const string RequiredMessage = "OPERATOR_REFERENCE_REQUIRED: Run this program through the current operator app after selecting its required references, or explicitly choose reference acquisition. Standalone execution cannot establish reference acceptance.";

    public RequireOperatorReferenceAcceptanceStep()
    {
        Name = "Check reference inputs";
        BreakConditionProperty.SetBreakCondition(this, BreakCondition.BreakOnError);
    }

    public override void PrePlanRun()
    {
        RequirePrepared();
        base.PrePlanRun();
    }

    public override void Run()
    {
        WaitIfPaused();
        var runtime = RequirePrepared();
        Log.Info(runtime.IsReferenceAcquisition
            ? "Reference acquisition only. Reference-dependent production acceptance is not evaluated."
            : "Operator reference inputs prepared for this run. The operator performs final reference acceptance after backend execution.");
        UpgradeVerdict(Verdict.Pass);
    }

    private IOperatorReferenceAcceptanceRuntime RequirePrepared()
    {
        if (((IRuntimeAwareStep)this).Runtime is IOperatorReferenceAcceptanceRuntime { IsOperatorReferenceAcceptancePrepared: true } runtime)
            return runtime;
        Log.Error(RequiredMessage);
        throw new InvalidOperationException(RequiredMessage);
    }
}
