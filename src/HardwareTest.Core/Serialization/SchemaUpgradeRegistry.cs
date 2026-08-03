using Serilog;

namespace HardwareTest.Core.Serialization;

/// One registered upgrade from FromVersion to ToVersion for a document type.
public sealed class SchemaUpgradeStep
{
    public required string DocumentType { get; init; }
    public required int FromVersion { get; init; }
    public required int ToVersion { get; init; }
    /// Optional in-place transform; null means identity.
    public Action<object>? Transform { get; init; }
}

/// Registry of schema upgrade steps. Production current versions are 1; a no-op 1→2
/// step is registered so the hook is exercised before any real bump.
public static class SchemaUpgradeRegistry
{
    private static readonly object Gate = new();
    private static readonly List<SchemaUpgradeStep> Steps =
    [
        // No-op registration — delivers the upgrade hook without changing shipped shape (still v1).
        new()
        {
            DocumentType = SchemaDocumentTypes.TestRunRecord,
            FromVersion = 1,
            ToVersion = 2,
            Transform = null,
        },
    ];

    public static IReadOnlyList<SchemaUpgradeStep> RegisteredSteps
    {
        get
        {
            lock (Gate)
            {
                return Steps.ToArray();
            }
        }
    }

    public static void Register(SchemaUpgradeStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.ToVersion <= step.FromVersion)
        {
            throw new ArgumentException("ToVersion must be greater than FromVersion.", nameof(step));
        }

        lock (Gate)
        {
            Steps.Add(step);
        }
    }

    /// Applies chained steps from <paramref name="fromVersion"/> toward <paramref name="targetVersion"/>.
    /// Returns the version reached (may equal fromVersion when no steps apply).
    public static int Apply(string documentType, int fromVersion, int targetVersion, object? document = null)
    {
        if (fromVersion >= targetVersion)
        {
            return fromVersion;
        }

        var version = fromVersion;
        while (version < targetVersion)
        {
            SchemaUpgradeStep? step;
            lock (Gate)
            {
                step = Steps.FirstOrDefault(s =>
                    string.Equals(s.DocumentType, documentType, StringComparison.Ordinal)
                    && s.FromVersion == version
                    && s.ToVersion <= targetVersion);
            }

            if (step is null)
            {
                Log.Debug(
                    "No schema upgrade step for {DocumentType} from {Version} toward {Target}; leaving at {Version}",
                    documentType,
                    version,
                    targetVersion,
                    version);
                break;
            }

            step.Transform?.Invoke(document!);
            Log.Debug(
                "Applied schema upgrade {DocumentType} {From} → {To} ({Mode})",
                documentType,
                step.FromVersion,
                step.ToVersion,
                step.Transform is null ? "identity" : "transform");
            version = step.ToVersion;
        }

        return version;
    }
}
