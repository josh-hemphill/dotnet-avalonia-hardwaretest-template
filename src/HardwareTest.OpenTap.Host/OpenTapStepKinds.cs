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

    /// True when <paramref name="type"/> is a Basic or InstrumentComponents.OpenTap
    /// step whose name is one of <paramref name="typeNames"/>. Used so tests can
    /// cover library type names without putting <see cref="TestStep"/> subclasses
    /// in the test assembly (those are picked up by PluginManager.Search).
    internal static bool MatchesAuthoringStepType(Type type, params string[] typeNames)
    {
        ArgumentNullException.ThrowIfNull(type);
        var ns = type.Namespace ?? string.Empty;
        if (!IsAuthoringNamespace(ns))
        {
            return false;
        }

        foreach (var name in typeNames)
        {
            if (string.Equals(type.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeNameIs(ITestStep step, string typeName)
        => MatchesAuthoringStepType(step.GetType(), typeName);

    private static bool IsAuthoringNamespace(string ns)
        => IsExactOrChildNamespace(ns, "InstrumentComponents.OpenTap")
           || IsExactOrChildNamespace(ns, "HardwareTest.OpenTap.Plugins.Basic");

    private static bool IsExactOrChildNamespace(string ns, string prefix)
        => string.Equals(ns, prefix, StringComparison.Ordinal)
           || ns.StartsWith(prefix + ".", StringComparison.Ordinal);
}
