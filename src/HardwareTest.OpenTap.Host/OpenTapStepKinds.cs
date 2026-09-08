using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Recognizes HardwareTest Basic and InstrumentComponents.OpenTap step types without a library compile reference.
public static class OpenTapStepKinds
{
    public static bool IsIdentity(ITestStep step)
        => step is IdentityCheckStep
           || TypeNameIs(step, "IdentityCheckStep")
           || TypeNameIs(step, "IdentityQueryStep");

    public static bool IsSafeShutdown(ITestStep step)
        => step is SafeShutdownStep || TypeNameIs(step, "SafeShutdownStep");

    public static bool IsOperatorInteraction(ITestStep step)
        => step is OperatorPromptStep or OperatorInputStep
           || TypeNameIs(step, "OperatorPromptStep")
           || TypeNameIs(step, "OperatorInputStep");

    public static bool IsPresentationExempt(ITestStep step)
        => IsIdentity(step)
           || IsSafeShutdown(step)
           || IsOperatorInteraction(step)
           || step is HangForeverStep
           || step is RepeatLoopStep
           || step is TestGroupStep
           || TypeNameIs(step, "HangForeverStep")
           || TypeNameIs(step, "RepeatLoopStep")
           || TypeNameIs(step, "TestGroupStep");

    /// Basic IdentityCheckStep still needs a HardwareDut; library Identity Query does not.
    public static bool RequiresHardwareDut(ITestStep step)
        => step is IdentityCheckStep || TypeNameIs(step, "IdentityCheckStep");

    private static bool TypeNameIs(ITestStep step, string typeName)
        => string.Equals(step.GetType().Name, typeName, StringComparison.Ordinal);
}
