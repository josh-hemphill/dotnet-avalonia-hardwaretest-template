using System.Text;
using System.Text.Json;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

public enum PlanContractSeverity
{
    Warning,
    Error,
}

public sealed record PlanContractFinding(
    PlanContractSeverity Severity,
    string Code,
    string Message,
    string? Path = null);

public sealed class PlanContractReport
{
    public required string TargetPath { get; init; }
    public IReadOnlyList<PlanContractFinding> Findings { get; init; } = [];

    public bool HasErrors => Findings.Any(f => f.Severity == PlanContractSeverity.Error);

    public int ErrorCount => Findings.Count(f => f.Severity == PlanContractSeverity.Error);

    public int WarningCount => Findings.Count(f => f.Severity == PlanContractSeverity.Warning);
}

public sealed class PlanContractBatchReport
{
    public IReadOnlyList<PlanContractReport> Plans { get; init; } = [];

    public bool HasErrors => Plans.Any(p => p.HasErrors);

    public int ErrorCount => Plans.Sum(p => p.ErrorCount);

    public int WarningCount => Plans.Sum(p => p.WarningCount);
}

/// Authoring checks for TapPlans destined for the HardwareTest Run board.
/// Does not gate operator Run — warnings stay informational for the engineer loop.
public static class PlanContractValidator
{
    public const int ChromeUsefulNestDepth = 3;

    public static class Codes
    {
        public const string FileNotFound = "FILE_NOT_FOUND";
        public const string NoPlans = "NO_PLANS";
        public const string PlanLoadFailed = "PLAN_LOAD_FAILED";
        public const string DuplicateLeafPath = "DUPLICATE_LEAF_PATH";
        public const string NestDepth = "NEST_DEPTH";
        public const string MissingSafeShutdown = "MISSING_SAFE_SHUTDOWN";
        public const string NoRebindableSlot = "NO_REBINDABLE_SLOT";
        public const string DialogStep = "DIALOG_STEP";
        public const string SidecarMissing = "SIDECAR_MISSING";
        public const string SidecarInvalid = "SIDECAR_INVALID";
        public const string SidecarEmpty = "SIDECAR_EMPTY";
        public const string PresentationTimeseriesOnly = "PRESENTATION_TIMESERIES_ONLY";
    }

    public static PlanContractBatchReport Validate(
        IReadOnlyList<string> targets,
        AppSettings? settings = null,
        bool trustConfiguredPluginDirectories = false)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            return new PlanContractBatchReport
            {
                Plans =
                [
                    new PlanContractReport
                    {
                        TargetPath = string.Empty,
                        Findings =
                        [
                            Error(Codes.NoPlans, "Pass a .TapPlan file or a directory of plans."),
                        ],
                    },
                ],
            };
        }

        var reports = new List<PlanContractReport>();
        foreach (var target in targets)
        {
            foreach (var path in ExpandTarget(target))
            {
                reports.Add(ValidateFile(path, settings, trustConfiguredPluginDirectories));
            }
        }

        return new PlanContractBatchReport { Plans = reports };
    }

    public static PlanContractReport ValidateFile(
        string tapPlanPath,
        AppSettings? settings = null,
        bool trustConfiguredPluginDirectories = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tapPlanPath);
        var findings = new List<PlanContractFinding>();
        if (!File.Exists(tapPlanPath))
        {
            if (Directory.Exists(tapPlanPath))
            {
                findings.Add(Error(
                    Codes.NoPlans,
                    $"No .TapPlan files in directory '{tapPlanPath}'."));
            }
            else
            {
                findings.Add(Error(Codes.FileNotFound, $"Test plan not found: {tapPlanPath}"));
            }

            return new PlanContractReport { TargetPath = tapPlanPath, Findings = findings };
        }

        var includeCleanup = true;
        AnalyzeSidecar(tapPlanPath, findings, ref includeCleanup);

        try
        {
            // Load-only: do not Register a VISA broker. Typed instrument XML deserializes
            // without Open(); registering would leak into the serial OpenTapSerial suite.
            var catalog = new OpenTapHostCatalog(
                settings ?? new AppSettings { UseMockVisa = true },
                Serilog.Log.Logger.ForContext(typeof(PlanContractValidator)),
                visaBroker: null,
                trustConfiguredPluginDirectories: trustConfiguredPluginDirectories);
            catalog.EnsurePlugins();
            var plan = TestPlan.Load(tapPlanPath);
            AnalyzePlan(plan, includeCleanup, findings);
        }
        catch (Exception ex)
        {
            findings.Add(Error(Codes.PlanLoadFailed, $"Failed to load plan: {ex.Message}"));
        }

        return new PlanContractReport { TargetPath = tapPlanPath, Findings = findings };
    }

    public static string Format(PlanContractBatchReport batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var sb = new StringBuilder();
        if (batch.Plans.Count == 0)
        {
            sb.AppendLine("No plans to validate.");
            return sb.ToString();
        }

        foreach (var report in batch.Plans)
        {
            var label = string.IsNullOrWhiteSpace(report.TargetPath) ? "(no path)" : report.TargetPath;
            var status = report.HasErrors ? "FAIL" : "OK";
            sb.Append(status);
            sb.Append("  ");
            sb.Append(label);
            sb.Append("  (");
            sb.Append(report.ErrorCount);
            sb.Append(" error");
            if (report.ErrorCount != 1)
            {
                sb.Append('s');
            }

            sb.Append(", ");
            sb.Append(report.WarningCount);
            sb.Append(" warning");
            if (report.WarningCount != 1)
            {
                sb.Append('s');
            }

            sb.Append(')');
            sb.AppendLine();
            foreach (var finding in report.Findings)
            {
                sb.Append("  ");
                sb.Append(finding.Severity == PlanContractSeverity.Error ? "error" : "warning");
                sb.Append(' ');
                sb.Append(finding.Code);
                sb.Append("  ");
                sb.Append(finding.Message);
                if (!string.IsNullOrWhiteSpace(finding.Path))
                {
                    sb.Append("  [");
                    sb.Append(finding.Path);
                    sb.Append(']');
                }

                sb.AppendLine();
            }
        }

        sb.Append(batch.Plans.Count);
        sb.Append(batch.Plans.Count == 1 ? " plan" : " plans");
        sb.Append(", ");
        sb.Append(batch.ErrorCount);
        sb.Append(batch.ErrorCount == 1 ? " error" : " errors");
        sb.Append(", ");
        sb.Append(batch.WarningCount);
        sb.Append(batch.WarningCount == 1 ? " warning." : " warnings.");
        sb.AppendLine();
        return sb.ToString();
    }

    public static int ExitCode(PlanContractBatchReport batch)
        => batch.HasErrors ? 1 : 0;

    internal static IReadOnlyList<string> ExpandTarget(string target)
    {
        if (Directory.Exists(target))
        {
            var files = Directory.EnumerateFiles(target, "*.TapPlan", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return files.Count == 0 ? [target] : files;
        }

        return [target];
    }

    private static void AnalyzeSidecar(
        string tapPlanPath,
        List<PlanContractFinding> findings,
        ref bool includeCleanup)
    {
        var id = Path.GetFileNameWithoutExtension(tapPlanPath);
        var sidecarPath = Path.Combine(Path.GetDirectoryName(tapPlanPath) ?? string.Empty, $"{id}.program.json");
        if (!File.Exists(sidecarPath))
        {
            findings.Add(Warning(
                Codes.SidecarMissing,
                $"Missing sidecar {id}.program.json beside {Path.GetFileName(tapPlanPath)}. Copy plans/opentap/template.program.json."));
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(
                File.ReadAllText(sidecarPath),
                ProgramCatalogJsonContext.Default.ProgramSidecar);
            if (parsed is null)
            {
                findings.Add(Error(Codes.SidecarEmpty, $"Empty sidecar {id}.program.json"));
                return;
            }

            includeCleanup = parsed.SelectionIncludesCleanup ?? true;
        }
        catch (Exception ex)
        {
            findings.Add(Error(Codes.SidecarInvalid, $"Invalid sidecar {id}.program.json: {ex.Message}"));
        }
    }

    private static void AnalyzePlan(TestPlan plan, bool includeCleanup, List<PlanContractFinding> findings)
    {
        var tree = OpenTapStepTree.Build(plan);
        var leaves = Flatten(tree).Where(n => n.Children.Count == 0).ToList();
        var duplicatePaths = leaves
            .GroupBy(l => l.Path, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        foreach (var path in duplicatePaths)
        {
            findings.Add(Error(
                Codes.DuplicateLeafPath,
                "Duplicate leaf path; rename a sibling or nest under unique groups so Run Selected can address this step.",
                path));
        }

        var nestDepth = MaxGroupDepth(plan, 0);
        if (nestDepth > ChromeUsefulNestDepth)
        {
            findings.Add(Warning(
                Codes.NestDepth,
                $"Nest depth {nestDepth} exceeds chrome-useful depth {ChromeUsefulNestDepth} (Stages → Sections → Nested). Deeper groups still run as leaves."));
        }

        var hasSafeShutdown = OpenTapStepTree.Flatten(plan).Any(s => s is SafeShutdownStep);
        if (includeCleanup && !hasSafeShutdown)
        {
            findings.Add(Error(
                Codes.MissingSafeShutdown,
                "Plan has no SafeShutdownStep. Add one, or set selectionIncludesCleanup: false in the sidecar for software-only selection."));
        }

        var hasRebindable = InstrumentResourceAccess.CollectFromPlan(plan).Any(InstrumentResourceAccess.HasWritableResourceProperty);
        if (!hasRebindable)
        {
            findings.Add(Error(
                Codes.NoRebindableSlot,
                "Plan has no rebindable instrument slot (writable VisaAddress, ResourceName, or Address). Instruments cannot bind this plan."));
        }

        foreach (var step in OpenTapStepTree.Flatten(plan))
        {
            if (!LooksLikeDialogStep(step))
            {
                continue;
            }

            var path = Flatten(tree).FirstOrDefault(n => string.Equals(n.Id, step.Id.ToString(), StringComparison.OrdinalIgnoreCase))?.Path
                       ?? step.Name;
            findings.Add(Error(
                Codes.DialogStep,
                $"Step '{step.Name}' looks like an OpenTAP/OS dialog. Use OperatorPromptStep / OperatorInputStep (in-panel) instead.",
                path));
        }

        var roles = new List<string>();
        foreach (var step in OpenTapStepTree.Flatten(plan))
        {
            if (step.ChildTestSteps.Count > 0)
            {
                continue;
            }

            var hints = OpenTapPresentation.TryReadMixin(step);
            if (hints is null || string.IsNullOrWhiteSpace(hints.DisplayRole))
            {
                continue;
            }

            roles.Add(hints.DisplayRole);
        }

        if (roles.Count > 0
            && roles.All(r => string.Equals(r, PresentationDisplayRoles.Timeseries, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Warning(
                Codes.PresentationTimeseriesOnly,
                "Every Presentation mixin uses DisplayRole timeseries. Prefer scalar/passband for pass criteria (band-first cookbook)."));
        }
    }

    private static int MaxGroupDepth(ITestStepParent parent, int groupDepth)
    {
        var max = groupDepth;
        foreach (var child in parent.ChildTestSteps)
        {
            if (child.ChildTestSteps.Count > 0)
            {
                max = Math.Max(max, MaxGroupDepth(child, groupDepth + 1));
            }
        }

        return max;
    }

    private static bool LooksLikeDialogStep(ITestStep step)
    {
        var type = step.GetType();
        var name = type.Name;
        var full = type.FullName ?? string.Empty;
        if (name.Equals("DialogStep", StringComparison.OrdinalIgnoreCase)
            || name.Contains("MessageBox", StringComparison.OrdinalIgnoreCase)
            || full.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
            || full.Contains("System.Windows.MessageBox", StringComparison.OrdinalIgnoreCase)
            || full.Contains("OpenTap.Wpf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

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

    private static PlanContractFinding Error(string code, string message, string? path = null)
        => new(PlanContractSeverity.Error, code, message, path);

    private static PlanContractFinding Warning(string code, string message, string? path = null)
        => new(PlanContractSeverity.Warning, code, message, path);
}

/// Shared stdout runner for HardwareTest --validate-plan and HardwareTest.PlanValidate.
public static class PlanContractCli
{
    public const int UsageExitCode = 2;

    public static int Run(
        IReadOnlyList<string> targets,
        AppSettings? settings,
        TextWriter output,
        bool trustConfiguredPluginDirectories = false)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(output);
        if (targets.Count == 0 || targets.All(string.IsNullOrWhiteSpace))
        {
            output.WriteLine("Usage: pass a .TapPlan file or a directory of .TapPlan files.");
            output.WriteLine("HardwareTest --validate-plan <path>");
            output.WriteLine("HardwareTest.PlanValidate <path> [<path>...] [--opentap-plugin-dirs <dir>]");
            return UsageExitCode;
        }

        var batch = PlanContractValidator.Validate(
            targets.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            settings,
            trustConfiguredPluginDirectories);
        output.Write(PlanContractValidator.Format(batch));
        return PlanContractValidator.ExitCode(batch);
    }
}
