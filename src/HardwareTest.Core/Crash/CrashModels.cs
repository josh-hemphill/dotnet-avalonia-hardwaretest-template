namespace HardwareTest.Core.Crash;

public enum SafeStopOutcome
{
    NotAttempted = 0,
    Confirmed = 1,
    TimedOut = 2,
    Failed = 3,
}

/// One link in the exception chain captured for a dossier.
public sealed class CrashExceptionFrame
{
    public string TypeName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
}

/// Primary crash.json document (schema-versioned, offline).
public sealed class CrashReportDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string DossierId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public bool IsFatal { get; set; }
    public string Source { get; set; } = string.Empty;
    public SafeStopOutcome SafeStopOutcome { get; set; }
    public string? AppVersion { get; set; }
    public string? AppCommitSha { get; set; }
    public string? InformationalVersion { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? RuntimeIdentifier { get; set; }
    public string? OsDescription { get; set; }
    public string? ProcessArchitecture { get; set; }
    public string? Culture { get; set; }
    public double UptimeSeconds { get; set; }
    public int FaultingThreadId { get; set; }
    public string? FaultingThreadName { get; set; }
    public string? ActiveRunId { get; set; }
    public string? ActivePlanId { get; set; }
    /// Exit code of a killed or crashed OpenTAP worker (schema 1 additive).
    public int? WorkerExitCode { get; set; }
    /// Tail of worker stderr captured at kill/crash (schema 1 additive).
    public string? WorkerStdErrTail { get; set; }
    public List<CrashExceptionFrame> Exceptions { get; set; } = [];
}

/// Redacted operator/session snapshot for session.json.
public sealed class CrashSessionSnapshot
{
    public bool DutPresent { get; set; }
    public string? PlanId { get; set; }
    public bool IsEngineerDebugMode { get; set; }
    public string? DutSerialRedacted { get; set; }
    public string? OperatorNameRedacted { get; set; }
    public string? CredentialSerialRedacted { get; set; }
}

/// One provenance row in config.json (values already redacted).
public sealed class CrashConfigProvenanceRow
{
    public string Key { get; set; } = string.Empty;
    public string EffectiveValue { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? SourceDetail { get; set; }
}

/// Redacted effective settings + provenance for config.json.
public sealed class CrashConfigSnapshot
{
    public List<CrashConfigProvenanceRow> Provenance { get; set; } = [];
}

/// Context collected at capture time (not all persisted as one blob).
public sealed class CrashCaptureContext
{
    public required CrashReportDocument Report { get; init; }
    public CrashConfigSnapshot? Config { get; init; }
    public CrashSessionSnapshot? Session { get; init; }
    public string LogTail { get; init; } = string.Empty;
    /// Raw identifier strings to scrub from log-tail.txt when redaction is on.
    public IReadOnlyList<string?> IdentifiersToRedact { get; init; } = [];
}

/// Summary of an unreviewed dossier for Home recovery banner.
public sealed class CrashDossierSummary
{
    public required string DossierId { get; init; }
    public required string DirectoryPath { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public bool IsFatal { get; init; }
    public string? AppVersion { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
}
