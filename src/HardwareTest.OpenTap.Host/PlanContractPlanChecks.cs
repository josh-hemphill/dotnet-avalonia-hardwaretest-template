using System.Reflection;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Presentation, identity, and limit checks for a loaded TapPlan.
internal static class PlanContractPlanChecks
{
    public static void Analyze(
        TestPlan plan,
        ProgramSidecar? sidecar,
        List<PlanContractFinding> findings)
    {
        AnalyzeIdentity(plan, sidecar, findings);
        AnalyzePresentation(plan, findings);
    }

    private static void AnalyzeIdentity(TestPlan plan, ProgramSidecar? sidecar, List<PlanContractFinding> findings)
    {
        if (sidecar?.RequireSerial is not true)
        {
            return;
        }

        var identities = OpenTapStepTree.Flatten(plan).OfType<IdentityCheckStep>().ToList();
        if (identities.Count == 0)
        {
            findings.Add(new PlanContractFinding(
                PlanContractSeverity.Error,
                PlanContractValidator.Codes.MissingIdentity,
                "Sidecar requireSerial is true but the plan has no IdentityCheckStep."));
            return;
        }

        if (identities.Any(step => step.Dut is null))
        {
            findings.Add(new PlanContractFinding(
                PlanContractSeverity.Error,
                PlanContractValidator.Codes.MissingDut,
                "IdentityCheckStep has no HardwareDut. DUT confirm cannot stamp this plan."));
        }
    }

    private static void AnalyzePresentation(TestPlan plan, List<PlanContractFinding> findings)
    {
        var tree = OpenTapStepTree.Build(plan);
        var leaves = Flatten(tree).Where(n => n.Children.Count == 0).ToList();
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var leaf in leaves)
        {
            var step = FindStep(plan, leaf.Id);
            if (step is null || IsPresentationExempt(step))
            {
                continue;
            }

            var hints = OpenTapPresentation.TryReadMixin(step);
            if (hints is null || string.IsNullOrWhiteSpace(hints.DisplayRole))
            {
                findings.Add(new PlanContractFinding(
                    PlanContractSeverity.Warning,
                    PlanContractValidator.Codes.MissingPresentation,
                    "Measure/analyze leaf has no Presentation mixin. Attach Presentation (ChannelKey + DisplayRole) so Band/Typst can map this step.",
                    leaf.Path));
                continue;
            }

            if (string.IsNullOrWhiteSpace(hints.ChannelKey))
            {
                findings.Add(new PlanContractFinding(
                    PlanContractSeverity.Warning,
                    PlanContractValidator.Codes.EmptyChannelKey,
                    "Presentation mixin has an empty ChannelKey.",
                    leaf.Path));
            }
            else if (keys.TryGetValue(hints.ChannelKey, out var existing))
            {
                findings.Add(new PlanContractFinding(
                    PlanContractSeverity.Error,
                    PlanContractValidator.Codes.DuplicateChannelKey,
                    $"Presentation ChannelKey '{hints.ChannelKey}' is already used by '{existing}'. ChannelKey must stay unique.",
                    leaf.Path));
            }
            else
            {
                keys[hints.ChannelKey] = leaf.Path;
            }

            if (IsBandRole(hints.DisplayRole) && !HasLimitSetting(step))
            {
                findings.Add(new PlanContractFinding(
                    PlanContractSeverity.Warning,
                    PlanContractValidator.Codes.MissingLimits,
                    $"DisplayRole '{hints.DisplayRole}' has no LimitLow, LimitHigh, or Threshold on the step. Band/Typst pass criteria need limits.",
                    leaf.Path));
            }
        }
    }

    private static bool IsPresentationExempt(ITestStep step)
        => step is IdentityCheckStep
            or OperatorPromptStep
            or OperatorInputStep
            or SafeShutdownStep
            or HangForeverStep
            or RepeatLoopStep
            or TestGroupStep;

    private static bool IsBandRole(string role)
        => string.Equals(role, PresentationDisplayRoles.Scalar, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, PresentationDisplayRoles.Passband, StringComparison.OrdinalIgnoreCase);

    private static bool HasLimitSetting(ITestStep step)
    {
        var type = step.GetType();
        foreach (var name in new[] { "LimitLow", "LimitHigh", "Threshold" })
        {
            var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (prop is null || !prop.CanRead)
            {
                continue;
            }

            if (prop.GetValue(step) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static ITestStep? FindStep(TestPlan plan, string id)
        => OpenTapStepTree.Flatten(plan)
            .FirstOrDefault(s => string.Equals(s.Id.ToString(), id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<OpenTapStepNode> Flatten(IEnumerable<OpenTapStepNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
