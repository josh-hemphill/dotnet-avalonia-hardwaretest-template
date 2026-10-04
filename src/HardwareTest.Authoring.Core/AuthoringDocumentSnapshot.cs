using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// An isolated document value. Restoring always returns a fresh mutable object graph.
public sealed class AuthoringDocumentSnapshot
{
    private readonly ProgramDraft _draft;
    private static readonly JsonSerializerOptions IdentityOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private AuthoringDocumentSnapshot(ProgramDraft draft)
    {
        _draft = Clone(draft);
        PlanIdentity = Canonical(JsonSerializer.Serialize(new
        {
            _draft.PlanId,
            _draft.Instruments,
            Setup = _draft.Setup.Select(SetupValue).ToArray(),
            Measure = _draft.Measure.Select(NodeValue).ToArray(),
            _draft.Cleanup,
            _draft.AuthoringState
        }, IdentityOptions));
        SidecarIdentity = Canonical(JsonSerializer.Serialize(_draft.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar));
    }

    public string PlanIdentity { get; }
    public string SidecarIdentity { get; }
    public static AuthoringDocumentSnapshot Capture(ProgramDraft draft) => new(draft);
    public ProgramDraft Restore() => Clone(_draft);
    public bool ContentEquals(AuthoringDocumentSnapshot other)
        => PlanIdentity == other.PlanIdentity && SidecarIdentity == other.SidecarIdentity;

    private static ProgramDraft Clone(ProgramDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var sidecar = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(draft.Sidecar, ProgramCatalogJsonContext.Default.ProgramSidecar),
            ProgramCatalogJsonContext.Default.ProgramSidecar)!;
        return draft with
        {
            Sidecar = sidecar,
            AuthoringState = draft.AuthoringState.Clone(),
            Instruments = draft.Instruments.Select(i => i with { }).ToArray(),
            Setup = draft.Setup.Select(CloneSetup).ToArray(),
            Measure = draft.Measure.Select(CloneNode).ToArray(),
            Cleanup = draft.Cleanup with { InstrumentSlots = draft.Cleanup.InstrumentSlots.ToArray() }
        };
    }

    private static SetupAction CloneSetup(SetupAction action) => action switch
    {
        IdentitySetup value => value with { },
        OperatorPromptSetup value => value with { },
        OperatorInputSetup value => value with { },
        _ => throw Unsupported(action)
    };

    private static MeasureNode CloneNode(MeasureNode node) => node switch
    {
        MetricNode value => value with
        {
            Metric = value.Metric with
            {
                Limits = value.Metric.Limits is { } limits ? limits with { } : null,
                History = value.Metric.History is { } history ? history with { } : null,
                Source = CloneSource(value.Metric.Source)
            }
        },
        RepeatNode value => value with { Children = value.Children.Select(CloneNode).ToArray() },
        RawStepNode value => value with { },
        _ => throw Unsupported(node)
    };

    private static MetricSource CloneSource(MetricSource source) => source switch
    {
        MeasureSource value => value with { Settings = CopySettings(value.Settings) },
        AlgorithmSource value => value with
        {
            InputChannelKeys = value.InputChannelKeys.ToArray(),
            Settings = CopySettings(value.Settings)
        },
        ExpressionAlgorithm value => value with { InputChannelKeys = value.InputChannelKeys.ToArray() },
        TransferFunctionAlgorithm value => value with
        {
            Numerator = value.Numerator.ToArray(),
            Denominator = value.Denominator.ToArray()
        },
        _ => throw Unsupported(source)
    };

    private static IReadOnlyDictionary<string, string> CopySettings(IReadOnlyDictionary<string, string> source)
        => source switch
        {
            Dictionary<string, string> dictionary => new Dictionary<string, string>(dictionary, dictionary.Comparer),
            SortedDictionary<string, string> dictionary => new SortedDictionary<string, string>(dictionary, dictionary.Comparer),
            SortedList<string, string> dictionary => new SortedList<string, string>(dictionary, dictionary.Comparer),
            _ => throw new NotSupportedException($"Cannot isolate settings while preserving the comparer of {source.GetType().FullName}.")
        };

    private static object SetupValue(SetupAction action) => new { Type = action.GetType().Name, Value = (object)action };
    private static object NodeValue(MeasureNode node) => node switch
    {
        MetricNode value => new
        {
            Type = nameof(MetricNode),
            value.NodeId,
            Metric = new
            {
                value.Metric.Name,
                value.Metric.ChannelKey,
                value.Metric.DisplayRole,
                value.Metric.YUnit,
                value.Metric.Limits,
                value.Metric.History,
                Source = new { Type = value.Metric.Source.GetType().Name, Value = (object)value.Metric.Source }
            }
        },
        RepeatNode value => new { Type = nameof(RepeatNode), value.NodeId, value.Count, Children = value.Children.Select(NodeValue).ToArray() },
        RawStepNode value => new { Type = nameof(RawStepNode), value.NodeId, value.TypeName, value.XmlFragment },
        _ => throw Unsupported(node)
    };

    private static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray()) WriteCanonical(writer, child);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static NotSupportedException Unsupported(object value)
        => new($"Document snapshots do not support {value.GetType().FullName}.");
}
