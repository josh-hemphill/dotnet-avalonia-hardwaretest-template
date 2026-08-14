using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Walks a plan (or subtree) and attaches a per-run <see cref="IStepRuntime"/>.
public static class StepRuntimeBinder
{
    public static void Attach(ITestStepParent parent, IStepRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(runtime);
        if (parent is IRuntimeAwareStep root)
        {
            root.AttachRuntime(runtime);
        }

        foreach (var step in Flatten(parent))
        {
            if (step is IRuntimeAwareStep aware)
            {
                aware.AttachRuntime(runtime);
            }
        }
    }

    public static void Detach(ITestStepParent parent)
        => Attach(parent, NoOpStepRuntime.Instance);

    public static IEnumerable<ITestStep> Flatten(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }
}
