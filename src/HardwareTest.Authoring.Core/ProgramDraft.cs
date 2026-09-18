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

/// MATLAB-flavored subset; not MATLAB. Lowers to a closed analyze step or fails FORMULA_NO_LOWER.
public sealed record ExpressionAlgorithm(
    IReadOnlyList<string> InputChannelKeys,
    string Source) : MetricSource;

/// Discrete SISO LTI. Coefficients from MATLAB export JSON or filter(b,a,x) sugar.
/// Does not compile to Expressions. Execute path is ApplyTransferFunctionStep.
public sealed record TransferFunctionAlgorithm(
    string InputChannelKey,
    IReadOnlyList<double> Numerator,
    IReadOnlyList<double> Denominator,
    double TsSeconds,
    string Method) : MetricSource;

public sealed record LimitSpec(double? Low, double? High, double? Threshold);

public sealed record HistorySpec(bool Enabled, double? WatchPercent, double? AlertPercent);

public sealed record CleanupPolicy(
    bool IncludeSafeShutdown,
    IReadOnlyList<string> InstrumentSlots,
    bool IncludeMeasureSlots = false)
{
    public CleanupPolicy(bool includeSafeShutdown, string instrumentSlot)
        : this(
            includeSafeShutdown,
            string.IsNullOrWhiteSpace(instrumentSlot) ? [] : [instrumentSlot.Trim()],
            false)
    {
    }

    public string InstrumentSlot
        => InstrumentSlots.Count == 0 ? string.Empty : InstrumentSlots[0];
}

/// Resolves which instrument slots Safe Shutdown should run.
public static class AuthoringCleanup
{
    public static IReadOnlyList<string> ResolveSlots(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new List<string>();
        foreach (var slot in draft.Cleanup.InstrumentSlots)
        {
            Add(slots, seen, slot);
        }

        if (draft.Cleanup.IncludeMeasureSlots)
        {
            foreach (var slot in MeasureSlots(draft))
            {
                Add(slots, seen, slot);
            }
        }

        if (slots.Count == 0 && draft.Cleanup.IncludeSafeShutdown)
        {
            Add(slots, seen, draft.Instruments.FirstOrDefault()?.SlotName);
        }

        return slots;
    }

    public static IReadOnlyList<string> MeasureSlots(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new List<string>();
        foreach (var action in draft.Setup)
        {
            if (action is IdentitySetup identity)
            {
                Add(slots, seen, identity.InstrumentSlot);
            }
        }

        foreach (var metric in AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure))
        {
            if (metric.Source is MeasureSource measure)
            {
                Add(slots, seen, measure.InstrumentSlot);
            }
        }

        return slots;
    }

    private static void Add(List<string> slots, HashSet<string> seen, string? slot)
    {
        var token = slot?.Trim();
        if (string.IsNullOrWhiteSpace(token) || !seen.Add(token))
        {
            return;
        }

        slots.Add(token);
    }
}

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
    public const string FormulaEval = "FORMULA_EVAL";
    public const string TfDenLeadingZero = "TF_DEN_LEADING_ZERO";
    public const string TfMissingElapsed = "TF_MISSING_ELAPSED";
    public const string TfImport = "TF_IMPORT";
    public const string TfGrid = "TF_GRID";
    public const string TfMethod = "TF_METHOD";
}
