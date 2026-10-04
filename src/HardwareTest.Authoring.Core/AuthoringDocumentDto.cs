using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public enum FormulaDeploymentIntent { Deploy, Explore }

/// Authoring values that cannot be represented by the valid, typed runtime draft.
public sealed class AuthoringDocumentState
{
    public Dictionary<string, string> IncompleteNumericText { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, FormulaDeploymentIntent> FormulaIntent { get; set; } = [];
    public static string FieldKey(Guid nodeId, string field) => $"{nodeId:D}/{field}";
    public AuthoringDocumentState Clone() => new()
    {
        IncompleteNumericText = new(IncompleteNumericText, StringComparer.Ordinal),
        FormulaIntent = new(FormulaIntent)
    };
}

/// Durable source document; deliberately contains no OpenTAP runtime instances.
public sealed record AuthoringDocumentDto
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public required string PlanId { get; set; }
    public bool RequiresCompilation { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? CompiledPlanHash { get; set; }
    public string? CompiledSidecarHash { get; set; }
    public required ProgramSidecar Sidecar { get; set; }
    public required InstrumentRef[] Instruments { get; set; }
    public required AuthoringSetupDto[] Setup { get; set; }
    public required AuthoringMeasureDto[] Measure { get; set; }
    public required CleanupPolicy Cleanup { get; set; }
    public AuthoringDocumentState State { get; set; } = new();

    public static AuthoringDocumentDto FromDraft(ProgramDraft draft, long revision = 0,
        AuthoringDocumentState? state = null, string? compiledPlanHash = null, string? compiledSidecarHash = null)
    {
        var isolated = AuthoringDocumentSnapshot.Capture(draft).Restore();
        return new()
        {
            PlanId = isolated.PlanId, Revision = revision, Sidecar = isolated.Sidecar,
            Instruments = isolated.Instruments.ToArray(), Setup = isolated.Setup.Select(AuthoringSetupDto.From).ToArray(),
            Measure = isolated.Measure.Select(AuthoringMeasureDto.From).ToArray(), Cleanup = isolated.Cleanup,
            State = (state ?? draft.AuthoringState).Clone(), CompiledPlanHash = compiledPlanHash, CompiledSidecarHash = compiledSidecarHash
        };
    }

    public ProgramDraft ToDraft()
    {
        if (SchemaVersion != CurrentSchemaVersion) throw new InvalidDataException($"Unsupported draft schema {SchemaVersion}.");
        if (Revision < 0 || Sidecar is null || Instruments is null || Setup is null || Measure is null || Cleanup is null || State is null ||
            State.IncompleteNumericText is null || State.FormulaIntent is null)
            throw new InvalidDataException("The authoring source document is incomplete.");
        var draft = new ProgramDraft(PlanId, Sidecar, Instruments, Setup.Select(s => s.ToAction()).ToArray(),
            Measure.Select(m => m.ToNode()).ToArray(), Cleanup) { AuthoringState = State.Clone() };
        var ids = AuthoringDependencyIndex.Build(draft).Nodes.Select(n => n.NodeId).ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Draft node identities must be unique and nonempty.");
        return AuthoringDocumentSnapshot.Capture(draft).Restore();
    }
}

public sealed class AuthoringSetupDto
{
    public required string Kind { get; set; }
    public required Guid NodeId { get; set; }
    public string? InstrumentSlot { get; set; }
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? Title { get; set; }
    public string? StringFieldId { get; set; }
    public string? NumberFieldId { get; set; }
    internal static AuthoringSetupDto From(SetupAction action) => action switch
    {
        IdentitySetup a => new() { Kind = "identity", NodeId = a.NodeId, InstrumentSlot = a.InstrumentSlot },
        OperatorPromptSetup a => new() { Kind = "prompt", NodeId = a.NodeId, Name = a.Name, Message = a.Message },
        OperatorInputSetup a => new() { Kind = "input", NodeId = a.NodeId, Name = a.Name, Title = a.Title, Message = a.Message, StringFieldId = a.StringFieldId, NumberFieldId = a.NumberFieldId },
        _ => throw new InvalidDataException("Unknown setup variant.")
    };
    internal SetupAction ToAction() => (Kind switch
    {
        "identity" => (SetupAction)new IdentitySetup(Need(InstrumentSlot)),
        "prompt" => new OperatorPromptSetup(Need(Name), Need(Message)),
        "input" => new OperatorInputSetup(Need(Name), Need(Title), Need(Message), StringFieldId, NumberFieldId),
        _ => throw new InvalidDataException($"Unknown setup discriminator '{Kind}'.")
    }) with { NodeId = NodeId };
    internal static T Need<T>(T? value) where T : class => value ?? throw new InvalidDataException("A required draft value is missing.");
}

public sealed class AuthoringMeasureDto
{
    public required string Kind { get; set; }
    public required Guid NodeId { get; set; }
    public int? Count { get; set; }
    public AuthoringMeasureDto[]? Children { get; set; }
    public string? TypeName { get; set; }
    public string? XmlFragment { get; set; }
    public string? Name { get; set; }
    public string? ChannelKey { get; set; }
    public string? DisplayRole { get; set; }
    public string? YUnit { get; set; }
    public LimitSpec? Limits { get; set; }
    public HistorySpec? History { get; set; }
    public AuthoringSourceDto? Source { get; set; }
    internal static AuthoringMeasureDto From(MeasureNode node) => node switch
    {
        RepeatNode n => new() { Kind = "repeat", NodeId = n.NodeId, Count = n.Count, Children = n.Children.Select(From).ToArray() },
        RawStepNode n => new() { Kind = "raw", NodeId = n.NodeId, TypeName = n.TypeName, XmlFragment = n.XmlFragment },
        MetricNode n => new() { Kind = "metric", NodeId = n.NodeId, Name = n.Metric.Name, ChannelKey = n.Metric.ChannelKey, DisplayRole = n.Metric.DisplayRole, YUnit = n.Metric.YUnit, Limits = n.Metric.Limits, History = n.Metric.History, Source = AuthoringSourceDto.From(n.Metric.Source) },
        _ => throw new InvalidDataException("Unknown measure variant.")
    };
    internal MeasureNode ToNode() => (Kind switch
    {
        "repeat" => (MeasureNode)new RepeatNode(Count ?? throw new InvalidDataException("Missing repeat count."), AuthoringSetupDto.Need(Children).Select(n => n.ToNode()).ToArray()),
        "raw" => new RawStepNode(AuthoringSetupDto.Need(TypeName), AuthoringSetupDto.Need(XmlFragment)),
        "metric" => new MetricNode(new(AuthoringSetupDto.Need(Name), AuthoringSetupDto.Need(ChannelKey), AuthoringSetupDto.Need(DisplayRole), AuthoringSetupDto.Need(YUnit), Limits, History, AuthoringSetupDto.Need(Source).ToSource())),
        _ => throw new InvalidDataException($"Unknown measure discriminator '{Kind}'.")
    }) with { NodeId = NodeId };
}

public sealed class AuthoringSourceDto
{
    public required string Kind { get; set; }
    public string? InstrumentSlot { get; set; }
    public string? FunctionId { get; set; }
    public Dictionary<string, string>? Settings { get; set; }
    public bool SettingsIgnoreCase { get; set; }
    public string? AlgorithmId { get; set; }
    public string[]? InputChannelKeys { get; set; }
    public string? Expression { get; set; }
    public string? InputChannelKey { get; set; }
    public double[]? Numerator { get; set; }
    public double[]? Denominator { get; set; }
    public double? TsSeconds { get; set; }
    public string? Method { get; set; }
    internal static AuthoringSourceDto From(MetricSource source) => source switch
    {
        MeasureSource s => new() { Kind = "measure", InstrumentSlot = s.InstrumentSlot, FunctionId = s.FunctionId, Settings = new(s.Settings, StringComparer.Ordinal), SettingsIgnoreCase = IgnoreCase(s.Settings) },
        AlgorithmSource s => new() { Kind = "algorithm", AlgorithmId = s.AlgorithmId, InputChannelKeys = s.InputChannelKeys.ToArray(), Settings = new(s.Settings, StringComparer.Ordinal), SettingsIgnoreCase = IgnoreCase(s.Settings) },
        ExpressionAlgorithm s => new() { Kind = "expression", InputChannelKeys = s.InputChannelKeys.ToArray(), Expression = s.Source },
        TransferFunctionAlgorithm s => new() { Kind = "transferFunction", InputChannelKey = s.InputChannelKey, Numerator = s.Numerator.ToArray(), Denominator = s.Denominator.ToArray(), TsSeconds = s.TsSeconds, Method = s.Method },
        _ => throw new InvalidDataException("Unknown source variant.")
    };
    private static bool IgnoreCase(IReadOnlyDictionary<string, string> settings) => settings switch
    {
        Dictionary<string, string> d => d.Comparer.Equals("a", "A"),
        SortedDictionary<string, string> d => d.Comparer.Compare("a", "A") == 0,
        SortedList<string, string> d => d.Comparer.Compare("a", "A") == 0,
        _ => false
    };
    internal MetricSource ToSource() => Kind switch
    {
        "measure" => new MeasureSource(AuthoringSetupDto.Need(InstrumentSlot), AuthoringSetupDto.Need(FunctionId), new Dictionary<string, string>(AuthoringSetupDto.Need(Settings), SettingsIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)),
        "algorithm" => new AlgorithmSource(AuthoringSetupDto.Need(AlgorithmId), AuthoringSetupDto.Need(InputChannelKeys), new Dictionary<string, string>(AuthoringSetupDto.Need(Settings), SettingsIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)),
        "expression" => new ExpressionAlgorithm(AuthoringSetupDto.Need(InputChannelKeys), AuthoringSetupDto.Need(Expression)),
        "transferFunction" => new TransferFunctionAlgorithm(AuthoringSetupDto.Need(InputChannelKey), AuthoringSetupDto.Need(Numerator), AuthoringSetupDto.Need(Denominator), TsSeconds ?? throw new InvalidDataException("Missing sample time."), AuthoringSetupDto.Need(Method)),
        _ => throw new InvalidDataException($"Unknown source discriminator '{Kind}'.")
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AuthoringDocumentDto))]
[JsonSerializable(typeof(AuthoringWorkspaceDto))]
public partial class AuthoringDocumentJsonContext : JsonSerializerContext;

public sealed record AuthoringWorkspaceDto
{
    public int SchemaVersion { get; set; } = AuthoringDocumentDto.CurrentSchemaVersion;
    public long Revision { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public required AuthoringManifest Manifest { get; set; }
}
