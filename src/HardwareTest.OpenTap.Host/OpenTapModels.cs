using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host;

public sealed record DutIdentity(string Serial, string? PartNumber = null, string? Revision = null, string Family = "generic");

public sealed record StationProfile(IReadOnlyDictionary<string, string> RoleToResource);

public sealed class OpenTapInstrumentSlot
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string RoleHint { get; init; }
    public string ResourceName { get; set; } = string.Empty;
}

public sealed class OpenTapProgress
{
    public required string Message { get; init; }
    public string? StepName { get; init; }
    public string? StepPath { get; init; }
    public string? StepId { get; init; }
    public string? Verdict { get; init; }
    public string? StatusText { get; init; }
    public string? KeyValue { get; init; }
    public double OverallPercent { get; init; }
    public bool IsCompleted { get; init; }
    public bool AwaitingOperator { get; init; }
    public string? OperatorPromptMessage { get; init; }
    public OperatorInteractionRequest? InteractionRequest { get; init; }
    public RunResult? Result { get; init; }
    public MeasurementSampleEvent? Sample { get; init; }
    /// 1-based iteration index for innermost active Repeat/Sweep loop.
    public int? IterationIndex { get; init; }
    public int? IterationTotal { get; init; }
    /// Convenience text such as "3/5" or "#3".
    public string? IterationText { get; init; }
}

public sealed record MeasurementSampleEvent(
    string Channel,
    int Index,
    double Value,
    DateTimeOffset Timestamp,
    string? MetricKey = null,
    string? DisplayRole = null,
    string? Unit = null,
    double? LimitLow = null,
    double? LimitHigh = null)
{
    /// Builds a live event from a normalized stored sample.
    public static MeasurementSampleEvent FromStored(StoredSample sample, int index = 0) => new(
        sample.Channel,
        index,
        sample.Value,
        sample.Timestamp,
        string.IsNullOrWhiteSpace(sample.MetricKey) ? null : sample.MetricKey,
        sample.DisplayRole,
        sample.Unit,
        sample.LimitLow,
        sample.LimitHigh);

    /// Metric grouping key for tiles/charts.
    public string EffectiveMetricKey
        => string.IsNullOrWhiteSpace(MetricKey) ? Channel : MetricKey!;
}

public sealed class OpenTapRunSummary
{
    public required string RunId { get; init; }
    public required string PlanName { get; init; }
    public required RunResult Result { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DutSerial { get; init; }
    public string? DutPartNumber { get; init; }
    public string? DutRevision { get; init; }
    public string? SessionId { get; init; }
    public string? OperatorName { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public List<StoredSample> Samples { get; init; } = [];
    public List<StepResultRecord> Steps { get; init; } = [];
    public string Verdict { get; init; } = "NotSet";
}

public sealed class OpenTapStepNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool Enabled { get; set; } = true;
    public string Verdict { get; set; } = "NotSet";
    public string StatusText { get; set; } = "Pending";
    public string? KeyValue { get; set; }
    public bool IsStage { get; set; }
    public List<OpenTapStepNode> Children { get; init; } = [];
}
