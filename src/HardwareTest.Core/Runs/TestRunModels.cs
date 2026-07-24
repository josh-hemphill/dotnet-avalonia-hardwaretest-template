using HardwareTest.Core.Hardware;

namespace HardwareTest.Core.Runs;

public enum RunResult
{
    Unknown = 0,
    Passed = 1,
    Failed = 2,
    Cancelled = 3,
    Error = 4,
}

public sealed class TestRunRecord
{
    public string RunId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? DutSerial { get; set; }
    public string? DutPartNumber { get; set; }
    public string? DutRevision { get; set; }
    public string? SessionId { get; set; }
    public string? OperatorName { get; set; }
    public string? Resource { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RunResult Result { get; set; } = RunResult.Unknown;
    public string? ErrorMessage { get; set; }
    public List<StepResultRecord> Steps { get; set; } = [];
    public List<StepAttemptSummary> StepAttempts { get; set; } = [];
    public List<StoredSample> Samples { get; set; } = [];
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ReportPdfPath { get; set; }
    public List<string> PlotImagePaths { get; set; } = [];
}

public sealed class SuiteRunRecord
{
    public string SuiteRunId { get; set; } = string.Empty;
    public string SuiteId { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RunResult Result { get; set; } = RunResult.Unknown;
    public string? ErrorMessage { get; set; }
    public List<TestRunRecord> PlanRuns { get; set; } = [];
    public string? ReportPdfPath { get; set; }
}

public sealed class StepResultRecord
{
    public string StepId { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public string StepPath { get; set; } = string.Empty;
    public int AttemptNumber { get; set; } = 1;
    public bool Passed { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

/// Rollup of attempts for one step path within an Operator Session.
public sealed class StepAttemptSummary
{
    public string StepPath { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public bool? LatestPassed { get; set; }
    public string? LatestMessage { get; set; }
    public List<StepResultRecord> Attempts { get; set; } = [];

    public string Display => AttemptCount == 0
        ? string.Empty
        : $"{AttemptCount} ({FailedCount}F/{PassedCount}P)";
}

public sealed class StoredSample
{
    public string Channel { get; set; } = string.Empty;
    public string StepPath { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
    /// Innermost loop iteration when the sample was published (1-based); null if outside a loop.
    public int? IterationIndex { get; set; }
    /// Path/name of the innermost active loop step when stamped.
    public string? LoopPath { get; set; }
    /// Stable metric id (Presentation ChannelKey or Channel/Name).
    public string MetricKey { get; set; } = string.Empty;
    /// Presentation DisplayRole (timeseries / scalar / passband).
    public string? DisplayRole { get; set; }
    /// Unit label from Presentation YUnit or Scalar Unit column.
    public string? Unit { get; set; }

    /// History / grouping key: MetricKey when set, otherwise Channel.
    public string EffectiveMetricKey
        => string.IsNullOrWhiteSpace(MetricKey) ? Channel : MetricKey;

    /// Operator-facing line for Results / detail (MetricKey, role, value, unit).
    public string ToDisplayLine()
    {
        var key = EffectiveMetricKey;
        var role = string.IsNullOrWhiteSpace(DisplayRole) ? null : DisplayRole;
        var unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit;
        var value = Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        var core = role is null ? $"{key}: {value}" : $"{key} [{role}] {value}";
        if (unit is not null)
        {
            core += $" {unit}";
        }

        return $"{core} @ {Timestamp:u}";
    }

    public static StoredSample From(MeasurementSample sample) => new()
    {
        Channel = sample.Channel,
        Timestamp = sample.Timestamp,
        Value = sample.Value,
    };
}

public sealed class TestRunProgress
{
    public required string RunId { get; init; }
    public required string Message { get; init; }
    public string? StepId { get; init; }
    public MeasurementSample? Sample { get; init; }
    public bool IsCompleted { get; init; }
    public RunResult? Result { get; init; }
}

public sealed class SuiteRunProgress
{
    public required string SuiteRunId { get; init; }
    public required string Message { get; init; }
    public string? PlanId { get; init; }
    public string? PlanName { get; init; }
    public int PlanIndex { get; init; }
    public int PlanCount { get; init; }
    public double OverallPercent { get; init; }
    public TestRunProgress? PlanProgress { get; init; }
    public bool IsCompleted { get; init; }
    public RunResult? Result { get; init; }
}
