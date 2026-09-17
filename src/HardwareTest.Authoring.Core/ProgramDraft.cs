using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Compiled/decompiled programs for an on-disk authoring workspace.
public sealed record DraftWorkspace(
    AuthoringWorkspace Files,
    IReadOnlyList<ProgramDraft> Programs);

/// One program: sidecar, instruments, setup, measure tree, cleanup.
public sealed record ProgramDraft(
    string PlanId,
    ProgramSidecar Sidecar,
    IReadOnlyList<InstrumentRef> Instruments,
    IReadOnlyList<SetupAction> Setup,
    IReadOnlyList<MeasureNode> Measure,
    CleanupPolicy Cleanup);

public abstract record MeasureNode;

public sealed record MetricNode(MetricDraft Metric) : MeasureNode;

public sealed record RepeatNode(int Count, IReadOnlyList<MeasureNode> Children) : MeasureNode;

public sealed record RawStepNode(string TypeName, string XmlFragment) : MeasureNode;

public sealed record InstrumentRef(
    string SlotName,
    string TypeId,
    string VisaAddress);

public abstract record SetupAction;

public sealed record IdentitySetup(string InstrumentSlot) : SetupAction;

public sealed record OperatorPromptSetup(string Name, string Message) : SetupAction;

public sealed record OperatorInputSetup(
    string Name,
    string Title,
    string Message,
    string? StringFieldId,
    string? NumberFieldId) : SetupAction;

public sealed record MetricDraft(
    string Name,
    string ChannelKey,
    string DisplayRole,
    string YUnit,
    LimitSpec? Limits,
    HistorySpec? History,
    MetricSource Source);

public abstract record MetricSource;

public sealed record MeasureSource(
    string InstrumentSlot,
    string FunctionId,
    IReadOnlyDictionary<string, string> Settings) : MetricSource;

public sealed record AlgorithmSource(
    string AlgorithmId,
    IReadOnlyList<string> InputChannelKeys,
    IReadOnlyDictionary<string, string> Settings) : MetricSource;

/// MATLAB-flavored subset; not MATLAB. Compiles to Expressions or a closed analyze step.
public sealed record ExpressionAlgorithm(
    IReadOnlyList<string> InputChannelKeys,
    string Source) : MetricSource;

/// Discrete SISO LTI. Coefficients from MATLAB export JSON or filter(b,a,x) sugar.
/// Does not compile to Expressions. Execute path is ApplyTransferFunctionStep (later area).
public sealed record TransferFunctionAlgorithm(
    string InputChannelKey,
    IReadOnlyList<double> Numerator,
    IReadOnlyList<double> Denominator,
    double TsSeconds,
    string Method) : MetricSource;

public sealed record LimitSpec(double? Low, double? High, double? Threshold);

public sealed record HistorySpec(bool Enabled, double? WatchPercent, double? AlertPercent);

public sealed record CleanupPolicy(bool IncludeSafeShutdown, string InstrumentSlot);

public static class AuthoringCompileCodes
{
    public const string DuplicateChannelKey = "DUPLICATE_CHANNEL_KEY";
    public const string UnknownFunction = "UNKNOWN_FUNCTION";
    public const string DialogStep = "DIALOG_STEP";
    public const string PlanIdMismatch = "PLAN_ID_MISMATCH";
    public const string MissingLimits = "MISSING_LIMITS";
    public const string TfStepUnavailable = "TF_STEP_UNAVAILABLE";
    public const string FormulaParse = "FORMULA_PARSE";
    public const string FormulaNoLower = "FORMULA_NO_LOWER";
}
