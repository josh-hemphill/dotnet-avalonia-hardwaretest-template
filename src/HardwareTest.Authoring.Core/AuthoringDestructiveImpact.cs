using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public enum CatalogDeletionKind { RequiredField, ReportKind, ProgramKind }

public sealed record DestructiveProgramImpact(string PlanId, string DisplayName, IReadOnlyList<string> Nodes);

/// A named, immutable review snapshot. Only the originating session can apply it.
public sealed class CatalogDeletionImpact
{
    internal CatalogDeletionImpact(Guid session, string workspaceRoot, CatalogDeletionKind kind, string target,
        string fingerprint, IEnumerable<DestructiveProgramImpact> affectedPrograms)
    {
        Session = session; WorkspaceRoot = workspaceRoot; Kind = kind; Target = target;
        ContentFingerprint = fingerprint; AffectedPrograms = Array.AsReadOnly(affectedPrograms.ToArray());
    }
    internal Guid Session { get; }
    public string WorkspaceRoot { get; }
    public CatalogDeletionKind Kind { get; }
    public string Target { get; }
    public string Scope => "Workspace catalog and affected program sidecars";
    public string OperationName => $"Remove {Kind switch { CatalogDeletionKind.RequiredField => "required field", CatalogDeletionKind.ReportKind => "report kind", _ => "program kind" }} '{Target}' from workspace";
    public string ContentFingerprint { get; }
    public IReadOnlyList<DestructiveProgramImpact> AffectedPrograms { get; }
}

public sealed class InstrumentRemovalImpact
{
    internal InstrumentRemovalImpact(Guid session, string workspaceRoot, string planId, string slot,
        string fingerprint, IEnumerable<string> replacements, IEnumerable<string> nodes)
    {
        Session = session; WorkspaceRoot = workspaceRoot; PlanId = planId; TargetSlot = slot;
        ContentFingerprint = fingerprint; CompatibleReplacementSlots = Array.AsReadOnly(replacements.ToArray());
        AffectedNodes = Array.AsReadOnly(nodes.ToArray());
    }
    internal Guid Session { get; }
    public string WorkspaceRoot { get; }
    public string PlanId { get; }
    public string TargetSlot { get; }
    public string Scope => $"Program '{PlanId}' only";
    public string OperationName => $"Remove instrument slot '{TargetSlot}' from program '{PlanId}'";
    public string ContentFingerprint { get; }
    public IReadOnlyList<string> CompatibleReplacementSlots { get; }
    public IReadOnlyList<string> AffectedNodes { get; }
}

public sealed class ProgramRemovalImpact
{
    internal ProgramRemovalImpact(Guid session, string workspaceRoot, string planId, string tapPlanPath, string sidecarPath, string fingerprint)
    {
        Session = session; WorkspaceRoot = workspaceRoot; PlanId = planId;
        TapPlanPath = tapPlanPath; SidecarPath = sidecarPath; ContentFingerprint = fingerprint;
    }
    internal Guid Session { get; }
    public string WorkspaceRoot { get; }
    public string PlanId { get; }
    public string TapPlanPath { get; }
    public string SidecarPath { get; }
    public string ContentFingerprint { get; }
    public string Scope => $"Program '{PlanId}' only";
    public string OperationName => $"Remove program '{PlanId}'?";
}

/// Explicit recursive canonical visitor: derived records must never disappear through base-type serialization.
internal static class AuthoringContentFingerprint
{
    public static string Compute(AuthoringWorkspace workspace, IReadOnlyList<ProgramDraft> programs,
        IReadOnlyList<DirtyProgramSummary> dirty, bool catalogDirty)
    {
        var text = new StringBuilder();
        void Add(object? value)
        {
            if (value is null) { text.Append("N;"); return; }
            var token = value switch { IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString()! };
            text.Append('V').Append(token.Length).Append(':').Append(token);
        }
        void Strings(IEnumerable<string> values) { var array = values.ToArray(); Add(array.Length); foreach (var v in array) Add(v); }
        void Settings(IReadOnlyDictionary<string, string> settings) { Add(settings.Count); foreach (var p in settings.OrderBy(p => p.Key, StringComparer.Ordinal)) { Add(p.Key); Add(p.Value); } }
        void Source(MetricSource source)
        {
            Add(source.GetType().FullName);
            switch (source)
            {
                case MeasureSource m: Add(m.InstrumentSlot); Add(m.FunctionId); Settings(m.Settings); break;
                case AlgorithmSource a: Add(a.InstrumentSlot); Add(a.AlgorithmId); Strings(a.InputChannelKeys); Settings(a.Settings); break;
                case ExpressionAlgorithm e: Strings(e.InputChannelKeys); Add(e.Source); break;
                case TransferFunctionAlgorithm t:
                    Add(t.InputChannelKey); Add(t.Numerator.Count); foreach (var n in t.Numerator) Add(n);
                    Add(t.Denominator.Count); foreach (var d in t.Denominator) Add(d);
                    Add(t.TsSeconds); Add(t.Method); break;
                default: throw new AuthoringWorkspaceException("Cannot review destructive changes with an unknown metric source.");
            }
        }
        void Nodes(IReadOnlyList<MeasureNode> nodes)
        {
            Add(nodes.Count);
            foreach (var node in nodes)
            {
                Add(node.GetType().FullName); Add(node.NodeId);
                switch (node)
                {
                    case RepeatNode r: Add(r.Count); Nodes(r.Children); break;
                    case RawStepNode r: Add(r.TypeName); Add(r.XmlFragment); break;
                    case MetricNode m:
                        var metric = m.Metric; Add(metric.Name); Add(metric.ChannelKey); Add(metric.DisplayRole); Add(metric.YUnit);
                        Add(metric.Limits is not null); Add(metric.Limits?.Low); Add(metric.Limits?.High); Add(metric.Limits?.Threshold);
                        Add(metric.History is not null); Add(metric.History?.Enabled); Add(metric.History?.WatchPercent); Add(metric.History?.AlertPercent);
                        Source(metric.Source); break;
                    default: throw new AuthoringWorkspaceException("Cannot review destructive changes with an unknown measure node.");
                }
            }
        }
        Add(workspace.Root); Add(workspace.IsReadOnly);
        var manifest = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest),
            AuthoringJsonContext.Default.AuthoringManifest)!;
        if (manifest.Catalogs is { } catalogs)
            catalogs.Hardware = catalogs.Hardware.Select(definition => definition with
            {
                Settings = definition.Settings.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            }).ToList();
        Add(JsonSerializer.Serialize(manifest, AuthoringJsonContext.Default.AuthoringManifest));
        Strings(workspace.TapPlanPaths); Add(programs.Count);
        foreach (var program in programs)
        {
            Add(program.PlanId); Add(JsonSerializer.Serialize(program.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
            Add(program.Instruments.Count); foreach (var i in program.Instruments) { Add(i.SlotName); Add(i.TypeId); Add(i.VisaAddress); Add(i.OpaqueResourceXml); Settings(i.Settings); }
            Add(program.Setup.Count);
            foreach (var setup in program.Setup)
            {
                Add(setup.GetType().FullName); Add(setup.NodeId);
                switch (setup)
                {
                    case IdentitySetup i: Add(i.InstrumentSlot); break;
                    case OperatorPromptSetup p: Add(p.Name); Add(p.Message); break;
                    case OperatorInputSetup i: Add(i.Name); Add(i.Title); Add(i.Message); Add(i.StringFieldId); Add(i.NumberFieldId); break;
                    default: throw new AuthoringWorkspaceException("Cannot review destructive changes with an unknown setup action.");
                }
            }
            Nodes(program.Measure); Add(program.Cleanup.NodeId); Add(program.Cleanup.IncludeSafeShutdown); Strings(program.Cleanup.InstrumentSlots); Add(program.Cleanup.IncludeMeasureSlots);
        }
        Add(catalogDirty); Add(dirty.Count); foreach (var d in dirty) { Add(d.PlanId); Add(d.PlanDirty); Add(d.SidecarDirty); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
