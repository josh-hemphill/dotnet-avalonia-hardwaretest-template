using System.Collections.Frozen;

namespace HardwareTest.Authoring;

/// Workspace templates are independent of program membership and runtime bindings.
public sealed record AuthoringHardwareDefinition(Guid Id, string Name, string TypeId, string Address,
    IReadOnlyDictionary<string, string> Settings);

public sealed record AuthoringHardwareRow(string SlotName, string TypeId, string Address,
    string PackageStatus, string Usage, string Cleanup);

public sealed class AuthoringHardwareEditImpact
{
    internal AuthoringHardwareEditImpact(Guid session, string workspaceRoot, string planId,
        string slotName, string contentFingerprint, InstrumentRef replacement, IEnumerable<string> affectedNodes)
    {
        Session = session; WorkspaceRoot = workspaceRoot; PlanId = planId; SlotName = slotName;
        ContentFingerprint = contentFingerprint;
        Replacement = replacement with { Settings = replacement.Settings.ToFrozenDictionary(StringComparer.Ordinal) };
        AffectedNodes = Array.AsReadOnly(affectedNodes.ToArray());
    }

    internal Guid Session { get; }
    public string WorkspaceRoot { get; }
    public string PlanId { get; }
    public string SlotName { get; }
    public string ContentFingerprint { get; }
    public InstrumentRef Replacement { get; }
    public IReadOnlyList<string> AffectedNodes { get; }
}

public sealed class AuthoringHardwareDefinitionImpact
{
    internal AuthoringHardwareDefinitionImpact(Guid session, string workspaceRoot,
        string contentFingerprint, Guid id, string name, IEnumerable<string> programConsequences)
    {
        Session = session; WorkspaceRoot = workspaceRoot; ContentFingerprint = contentFingerprint;
        Id = id; Name = name; ProgramConsequences = Array.AsReadOnly(programConsequences.ToArray());
    }

    internal Guid Session { get; }
    public string WorkspaceRoot { get; }
    public string ContentFingerprint { get; }
    public Guid Id { get; }
    public string Name { get; }
    public IReadOnlyList<string> ProgramConsequences { get; }
}
