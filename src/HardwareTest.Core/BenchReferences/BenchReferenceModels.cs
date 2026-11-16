using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.BenchReferences;

/// One authored, additional acceptance rule. Null tolerance is an editable incomplete draft.
public sealed record BenchReferenceRule
{
    public string RuleId { get; init; } = string.Empty;
    public string MetricKey { get; init; } = string.Empty;
    public Guid ProducerStepId { get; init; }
    public string ReferenceKey { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Comparison { get; init; } = BenchReferenceComparisons.Absolute;
    public double? Tolerance { get; init; }
}

public static class BenchReferenceComparisons
{
    public const string Absolute = "absolute";
    public const string Percent = "percent";
}

/// Effective compiled and bench context, excluding current DUT/operator and unrelated UI settings.
public sealed record BenchReferenceContext
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanSha256 { get; init; } = string.Empty;
    public string SidecarSha256 { get; init; } = string.Empty;
    public string ConfigurationSha256 { get; init; } = string.Empty;
    public string VisaMode { get; init; } = string.Empty;
}

public static class BenchReferenceVisaModes
{
    public const string Simulated = "simulated";
    public const string Physical = "physical";
}

public sealed record BenchReferenceValue
{
    public string ReferenceKey { get; init; } = string.Empty;
    public string MetricKey { get; init; } = string.Empty;
    public Guid ProducerStepId { get; init; }
    public string Unit { get; init; } = string.Empty;
    public double Value { get; init; }
    public int SampleCount { get; init; }
    public string Reduction { get; init; } = "arithmetic-mean";
}

/// Immutable after publication. Replacement is a newly identified profile, never an overwrite.
public sealed record BenchReferenceProfile
{
    public int SchemaVersion { get; init; } = SchemaVersions.BenchReferenceProfile;
    public string Id { get; init; } = string.Empty;
    public int Revision { get; init; } = 1;
    public string Name { get; init; } = string.Empty;
    public string ContentSha256 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string SourceRunId { get; init; } = string.Empty;
    public string SourceDutSerial { get; init; } = string.Empty;
    public string SourceOperator { get; init; } = string.Empty;
    public DateTimeOffset SourceStartedAt { get; init; }
    public DateTimeOffset SourceCompletedAt { get; init; }
    public string SourceScope { get; init; } = string.Empty;
    public string? SourceSelectedStepPath { get; init; }
    public BenchReferenceContext Context { get; init; } = new();
    public List<BenchReferenceValue> Values { get; init; } = [];
}

/// A run-owned copy; mutable catalog files and their lifecycle are not consulted during evaluation.
public sealed record BenchReferenceUse
{
    public BenchReferenceRule Rule { get; init; } = new();
    public string ReferenceId { get; init; } = string.Empty;
    public int ReferenceRevision { get; init; }
    public string ReferenceName { get; init; } = string.Empty;
    public string ReferenceSha256 { get; init; } = string.Empty;
    public BenchReferenceValue ReferenceValue { get; init; } = new();
    public BenchReferenceContext Context { get; init; } = new();
    public string SourceRunId { get; init; } = string.Empty;
    public string SourceDutSerial { get; init; } = string.Empty;
    public string SourceOperator { get; init; } = string.Empty;
    public DateTimeOffset SourceStartedAt { get; init; }
    public DateTimeOffset SourceCompletedAt { get; init; }
    public string SourceScope { get; init; } = string.Empty;
    public string? SourceSelectedStepPath { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset AcceptedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record BenchReferenceEvaluation
{
    public string RuleId { get; init; } = string.Empty;
    public string MetricKey { get; init; } = string.Empty;
    public string ReferenceId { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Comparison { get; init; } = string.Empty;
    public double? Tolerance { get; init; }
    public double ReferenceValue { get; init; }
    public double? MeasuredValue { get; init; }
    public double? Deviation { get; init; }
    public bool Passed { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record BenchReferenceRetirement
{
    public int SchemaVersion { get; init; } = SchemaVersions.BenchReferenceRetirement;
    public string ReferenceId { get; init; } = string.Empty;
    public string ReferenceSha256 { get; init; } = string.Empty;
    public DateTimeOffset RetiredAt { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// A damaged/future record remains a visible catalog problem instead of becoming an absent default.
public sealed record BenchReferenceCatalogEntry
{
    public string Path { get; init; } = string.Empty;
    public BenchReferenceProfile? Profile { get; init; }
    public bool IsRetired { get; init; }
    public BenchReferenceRetirement? Retirement { get; init; }
    public string? Error { get; init; }
}

/// Identifies the exact profile revision and values the operator inspected before Run.
public sealed record BenchReferenceSelection
{
    public BenchReferenceRule Rule { get; init; } = new();
    public string ReferenceId { get; init; } = string.Empty;
    public int ExpectedRevision { get; init; }
    public string ExpectedSha256 { get; init; } = string.Empty;
}

public sealed record BenchReferenceAcceptance
{
    public DateTimeOffset AcceptedAt { get; init; }
    public IReadOnlyList<BenchReferenceUse> Uses { get; init; } = [];
}

public interface IBenchReferenceStore
{
    Task<IReadOnlyList<BenchReferenceCatalogEntry>> ListAsync(CancellationToken cancellationToken = default);
    /// Throws for absent, retired, corrupt, unsupported or tampered records; never substitutes another ID.
    Task<BenchReferenceProfile> LoadAsync(string id, CancellationToken cancellationToken = default);
    /// Publishes a new immutable ID. An existing destination, including a future schema, is never replaced.
    Task SaveAsync(BenchReferenceProfile profile, CancellationToken cancellationToken = default);
    Task RetireAsync(string id, string reason, CancellationToken cancellationToken = default);
    /// All selections are checked and frozen at one catalog transaction boundary. Stores without
    /// this capability must refuse execution rather than approximate it with independent reads.
    Task<BenchReferenceAcceptance> FreezeAsync(IReadOnlyList<BenchReferenceSelection> selections,
        BenchReferenceContext context, CancellationToken cancellationToken = default)
        => Task.FromException<BenchReferenceAcceptance>(new NotSupportedException("This reference store cannot atomically accept a complete reference selection."));
}
