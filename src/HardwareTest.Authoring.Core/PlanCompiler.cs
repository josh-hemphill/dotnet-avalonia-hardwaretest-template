using System.Text.Json;
using HardwareTest.OpenTap.Host;
using OpenTap;

namespace HardwareTest.Authoring;

public interface IPlanCompiler
{
    void Save(ProgramDraft draft, string tapPlanPath);
    ProgramDraft Load(string tapPlanPath);
    DraftWorkspace LoadAll(AuthoringWorkspace workspace);
    void SaveSidecar(string tapPlanPath, ProgramSidecar sidecar);
}

/// Compiles ProgramDraft to TapPlan + sidecar and decompiles the same files.
public sealed partial class PlanCompiler : IPlanCompiler
{
    public const string SetupGroupName = "Setup";
    public const string CleanupGroupName = "Cleanup";

    private readonly string[] _extraPluginDirectories;

    public PlanCompiler(IEnumerable<string>? extraPluginDirectories = null)
    {
        _extraPluginDirectories = extraPluginDirectories is null
            ? []
            : extraPluginDirectories.Where(dir => !string.IsNullOrWhiteSpace(dir)).ToArray();
    }

    public void Save(ProgramDraft draft, string tapPlanPath)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(tapPlanPath);

        var planId = Path.GetFileNameWithoutExtension(tapPlanPath);
        if (!string.Equals(draft.PlanId, planId, StringComparison.Ordinal))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.PlanIdMismatch}: draft '{draft.PlanId}' does not match '{planId}'.");
        }

        EnsureUniqueChannelKeys(draft.Measure);
        EnsureTransferFunctionClocks(draft);
        AuthoringPluginSearch.Search(_extraPluginDirectories);

        var directory = Path.GetDirectoryName(Path.GetFullPath(tapPlanPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plan = BuildPlan(draft);
        AssertNoDialog(plan);
        plan.Save(tapPlanPath);
        AuthoringCleanup.SyncSidecar(draft.Sidecar, draft.Cleanup);
        WriteSidecar(tapPlanPath, draft.Sidecar);
    }

    public ProgramDraft Load(string tapPlanPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tapPlanPath);
        if (!File.Exists(tapPlanPath))
        {
            throw new AuthoringWorkspaceException($"Test plan not found: {tapPlanPath}");
        }

        AuthoringPluginSearch.Search(_extraPluginDirectories);
        var plan = TestPlan.Load(tapPlanPath);
        var xmlById = IndexStepXml(tapPlanPath);
        var sidecar = ReadSidecar(tapPlanPath);
        return Decompile(Path.GetFileNameWithoutExtension(tapPlanPath), plan, sidecar, xmlById);
    }

    public DraftWorkspace LoadAll(AuthoringWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var programs = new List<ProgramDraft>(workspace.TapPlanPaths.Count);
        foreach (var path in workspace.TapPlanPaths)
        {
            programs.Add(Load(path));
        }

        return new DraftWorkspace(workspace, programs);
    }

    public void SaveSidecar(string tapPlanPath, ProgramSidecar sidecar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tapPlanPath);
        ArgumentNullException.ThrowIfNull(sidecar);
        WriteSidecar(tapPlanPath, sidecar);
    }

    internal static string SidecarPath(string tapPlanPath)
    {
        var id = Path.GetFileNameWithoutExtension(tapPlanPath);
        var dir = Path.GetDirectoryName(tapPlanPath) ?? string.Empty;
        return Path.Combine(dir, $"{id}.program.json");
    }

    internal static ProgramSidecar CloneSidecar(ProgramSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        var json = JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar);
        return JsonSerializer.Deserialize(json, ProgramCatalogJsonContext.Default.ProgramSidecar)
               ?? new ProgramSidecar();
    }

    private static void WriteSidecar(string tapPlanPath, ProgramSidecar sidecar)
    {
        var json = JsonSerializer.Serialize(sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar);
        File.WriteAllText(SidecarPath(tapPlanPath), json);
    }

    private static ProgramSidecar ReadSidecar(string tapPlanPath)
    {
        var path = SidecarPath(tapPlanPath);
        if (!File.Exists(path))
        {
            return new ProgramSidecar();
        }

        var raw = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ProgramSidecar();
        }

        return JsonSerializer.Deserialize(raw, ProgramCatalogJsonContext.Default.ProgramSidecar)
               ?? new ProgramSidecar();
    }

    private static void EnsureUniqueChannelKeys(IReadOnlyList<MeasureNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in EnumerateChannelKeys(nodes))
        {
            if (!seen.Add(key))
            {
                throw new AuthoringWorkspaceException(
                    $"{AuthoringCompileCodes.DuplicateChannelKey}: ChannelKey '{key}' is used more than once.");
            }
        }
    }

    private static IEnumerable<string> EnumerateChannelKeys(IReadOnlyList<MeasureNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MetricNode metric when !string.IsNullOrWhiteSpace(metric.Metric.ChannelKey):
                    yield return metric.Metric.ChannelKey.Trim();
                    break;
                case RepeatNode repeat:
                    foreach (var nested in EnumerateChannelKeys(repeat.Children))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static void AssertNoDialog(TestPlan plan)
    {
        foreach (var step in FlattenSteps(plan))
        {
            if (!LooksLikeDialog(step))
            {
                continue;
            }

            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.DialogStep}: step '{step.Name}' looks like an OpenTAP/OS dialog.");
        }
    }

    private static void EnsureTransferFunctionClocks(ProgramDraft draft)
    {
        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
        {
            MetricSource source;
            try
            {
                source = metric.Source is ExpressionAlgorithm expr
                    ? FormulaLowerer.Lower(expr, metric.Limits)
                    : metric.Source;
            }
            catch (AuthoringWorkspaceException)
            {
                source = metric.Source;
            }

            if (source is TransferFunctionAlgorithm tf)
            {
                EnsureTransferFunctionElapsed(draft, tf);
            }
        }
    }

    internal static bool LooksLikeDialog(ITestStep step)
    {
        var type = step.GetType();
        var name = type.Name;
        var full = type.FullName ?? string.Empty;
        return name.Equals("DialogStep", StringComparison.OrdinalIgnoreCase)
               || name.Contains("MessageBox", StringComparison.OrdinalIgnoreCase)
               || full.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
               || full.Contains("OpenTap.Wpf", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<ITestStep> FlattenSteps(ITestStepParent parent)
    {
        foreach (var child in parent.ChildTestSteps)
        {
            yield return child;
            foreach (var nested in FlattenSteps(child))
            {
                yield return nested;
            }
        }
    }
}
