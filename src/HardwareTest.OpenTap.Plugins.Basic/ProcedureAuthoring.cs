namespace HardwareTest.OpenTap.Plugins.Basic;

/// Optional capability implemented by plugin-owned procedure roots. Authoring uses the
/// serialized tree as source and the plugin's own validator for both preview and export.
public interface IProcedureAuthoring
{
    ProcedureEditorProfile EditorProfile { get; }
    void ValidateAuthoring();
    OperatorInteractionRequest? PreviewInteraction(Guid stepId, IReadOnlySet<string> completed) => null;
}

public sealed record ProcedureEditorProfile(int Version, IReadOnlyList<ProcedureStepEditor> Steps,
    IReadOnlyList<ProcedureDataEditor> DataFiles);

public sealed record ProcedureStepEditor(string Type, string Label, IReadOnlyList<ProcedureField> Fields,
    IReadOnlyList<string> ChildTypes, bool AllowAdd = false, bool HasOperatorPreview = false, bool IsHandoff = false);

public sealed record ProcedureDataEditor(string Path, string Label, IReadOnlyList<ProcedureTableEditor> Tables,
    IReadOnlyList<ProcedureField> HeaderFields);

public sealed record ProcedureTableEditor(string Key, string Label, IReadOnlyList<string> IdentityFields,
    IReadOnlyList<ProcedureField> Fields, IReadOnlyList<ProcedureTableEditor>? Children = null);

public enum ProcedureFieldKind { Text, Boolean, Integer, Number, TextList, NumberList, Instrument }

public sealed record ProcedureField(string Key, string Label, ProcedureFieldKind Kind = ProcedureFieldKind.Text,
    string? ReferenceTable = null, bool Optional = false, string? DefaultValue = null);

/// Optional runtime hook for plugins that include plan provenance with evaluated rows.
public interface IProcedureSourceIdentity
{
    void BindPlanSha256(string sha256);
}
