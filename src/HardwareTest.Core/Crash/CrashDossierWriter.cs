using System.Text;
using System.Text.Json;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest.Core.Crash;

/// Writes offline crash dossiers under {CrashDirectory}/{utc}-{id}/.
public sealed class CrashDossierWriter
{
    public const string ReviewedMarkerFileName = ".reviewed";
    public const int DefaultMaxLogTailChars = 256 * 1024;
    public const int DefaultMaxStackChars = 64 * 1024;

    private static int _reentrancy;

    public string CrashRoot { get; }
    public int RetentionCount { get; }
    public bool RedactIdentifiers { get; }

    public CrashDossierWriter(string crashRoot, int retentionCount = 20, bool redactIdentifiers = true)
    {
        CrashRoot = crashRoot;
        RetentionCount = Math.Clamp(retentionCount, 1, 500);
        RedactIdentifiers = redactIdentifiers;
    }

    /// Test helper: hold the write gate without writing.
    public static bool TryEnterWriteGateForTests()
        => Interlocked.CompareExchange(ref _reentrancy, 1, 0) == 0;

    public static void ExitWriteGateForTests()
        => Interlocked.Exchange(ref _reentrancy, 0);

    public static CrashDossierWriter FromSettings(AppSettings settings, string dataDirectory)
    {
        var root = string.IsNullOrWhiteSpace(settings.CrashDirectory)
            ? Path.Combine(dataDirectory, "crashes")
            : settings.CrashDirectory;
        return new CrashDossierWriter(root, settings.CrashRetentionCount, settings.RedactIdentifiersInDiagnostics);
    }

    /// Attempts to write a dossier. Returns the directory path, or null on failure / reentrancy.
    public string? TryWrite(CrashCaptureContext context)
    {
        if (Interlocked.CompareExchange(ref _reentrancy, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            return WriteCore(context);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Warning(ex, "Crash dossier write failed");
            }
            catch
            {
                // ignore
            }

            return null;
        }
        finally
        {
            Interlocked.Exchange(ref _reentrancy, 0);
        }
    }

    private string? WriteCore(CrashCaptureContext context)
    {
        CrashRedaction.EnsureSalt(Path.GetDirectoryName(CrashRoot));
        Directory.CreateDirectory(CrashRoot);

        var report = context.Report;
        if (string.IsNullOrWhiteSpace(report.DossierId))
        {
            report.DossierId = Guid.NewGuid().ToString("N")[..8];
        }

        if (report.CapturedAtUtc == default)
        {
            report.CapturedAtUtc = DateTimeOffset.UtcNow;
        }

        report.SchemaVersion = SchemaVersions.CrashReport;
        TruncateExceptions(report);

        var stamp = report.CapturedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssZ");
        var dir = Path.Combine(CrashRoot, $"{stamp}-{report.DossierId}");
        Directory.CreateDirectory(dir);

        TryWriteJson(Path.Combine(dir, "crash.json"), report, AppJsonContext.Default.CrashReportDocument);
        var logTail = context.LogTail ?? string.Empty;
        if (RedactIdentifiers && context.IdentifiersToRedact.Count > 0)
        {
            logTail = CrashRedaction.RedactText(logTail, context.IdentifiersToRedact, redact: true);
        }

        TryWriteText(Path.Combine(dir, "log-tail.txt"), Truncate(logTail, DefaultMaxLogTailChars));
        if (context.Config is not null)
        {
            TryWriteJson(Path.Combine(dir, "config.json"), context.Config, AppJsonContext.Default.CrashConfigSnapshot);
        }

        if (context.Session is not null)
        {
            TryWriteJson(Path.Combine(dir, "session.json"), context.Session, AppJsonContext.Default.CrashSessionSnapshot);
        }

        TryPruneRetention();
        return dir;
    }

    public void TryPruneRetention()
    {
        try
        {
            if (!Directory.Exists(CrashRoot))
            {
                return;
            }

            var dirs = Directory.GetDirectories(CrashRoot)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name, StringComparer.Ordinal)
                .ToArray();
            for (var i = RetentionCount; i < dirs.Length; i++)
            {
                try
                {
                    dirs[i].Delete(recursive: true);
                }
                catch
                {
                    // best effort
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    public IReadOnlyList<CrashDossierSummary> ListUnreviewed()
    {
        var results = new List<CrashDossierSummary>();
        try
        {
            if (!Directory.Exists(CrashRoot))
            {
                return results;
            }

            foreach (var dir in Directory.EnumerateDirectories(CrashRoot).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, ReviewedMarkerFileName)))
                    {
                        continue;
                    }

                    var crashPath = Path.Combine(dir, "crash.json");
                    if (!File.Exists(crashPath))
                    {
                        continue;
                    }

                    using var stream = File.OpenRead(crashPath);
                    var report = JsonSerializer.Deserialize(stream, AppJsonContext.Default.CrashReportDocument);
                    if (report is null)
                    {
                        continue;
                    }

                    var first = report.Exceptions.FirstOrDefault();
                    results.Add(new CrashDossierSummary
                    {
                        DossierId = report.DossierId,
                        DirectoryPath = dir,
                        CapturedAtUtc = report.CapturedAtUtc,
                        IsFatal = report.IsFatal,
                        AppVersion = report.InformationalVersion ?? report.AppVersion,
                        ExceptionType = first?.TypeName,
                        ExceptionMessage = first?.Message,
                    });
                }
                catch
                {
                    // skip bad dossier
                }
            }
        }
        catch
        {
            // ignore
        }

        return results;
    }

    public static bool TryMarkReviewed(string dossierDirectory)
    {
        try
        {
            File.WriteAllText(Path.Combine(dossierDirectory, ReviewedMarkerFileName), DateTimeOffset.UtcNow.ToString("u"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? TryExportZip(string dossierDirectory, string destinationZipPath)
    {
        try
        {
            var destDir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (File.Exists(destinationZipPath))
            {
                File.Delete(destinationZipPath);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(dossierDirectory, destinationZipPath);
            return destinationZipPath;
        }
        catch
        {
            return null;
        }
    }

    public static List<CrashExceptionFrame> CaptureExceptionChain(Exception? ex, bool redact, params string?[] identifiers)
    {
        var frames = new List<CrashExceptionFrame>();
        var current = ex;
        var depth = 0;
        while (current is not null && depth < 16)
        {
            frames.Add(new CrashExceptionFrame
            {
                TypeName = current.GetType().FullName ?? current.GetType().Name,
                Message = CrashRedaction.RedactText(current.Message, identifiers, redact),
                StackTrace = Truncate(
                    CrashRedaction.RedactText(current.StackTrace, identifiers, redact),
                    DefaultMaxStackChars),
            });
            current = current.InnerException;
            depth++;
        }

        return frames;
    }

    public static CrashConfigSnapshot BuildConfigSnapshot(
        IReadOnlyList<SettingProvenance> provenance,
        bool redact,
        params string?[] identifiers)
    {
        var rows = new List<CrashConfigProvenanceRow>();
        foreach (var row in provenance)
        {
            rows.Add(new CrashConfigProvenanceRow
            {
                Key = row.Key,
                EffectiveValue = CrashRedaction.RedactText(
                    CrashRedaction.RedactPath(row.EffectiveValue, redact),
                    identifiers,
                    redact),
                Source = row.Source.ToString(),
                SourceDetail = CrashRedaction.RedactText(
                    CrashRedaction.RedactPath(row.SourceDetail, redact),
                    identifiers,
                    redact),
            });
        }

        return new CrashConfigSnapshot { Provenance = rows };
    }

    public static CrashSessionSnapshot BuildSessionSnapshot(
        bool dutPresent,
        string? planId,
        bool engineerMode,
        string? dutSerial,
        string? operatorName,
        bool redact,
        string? credentialSerial = null)
        => new()
        {
            DutPresent = dutPresent,
            PlanId = planId,
            IsEngineerDebugMode = engineerMode,
            DutSerialRedacted = string.IsNullOrWhiteSpace(dutSerial)
                ? null
                : CrashRedaction.HashIdentifier(dutSerial, redact),
            OperatorNameRedacted = string.IsNullOrWhiteSpace(operatorName)
                ? null
                : CrashRedaction.HashIdentifier(operatorName, redact),
            CredentialSerialRedacted = string.IsNullOrWhiteSpace(credentialSerial)
                ? null
                : CrashRedaction.HashIdentifier(credentialSerial, redact),
        };

    public static CrashReportDocument BuildReport(
        Exception? exception,
        bool isFatal,
        string source,
        SafeStopOutcome safeStop,
        BuildInfo? buildInfo,
        string? activeRunId,
        string? activePlanId,
        bool redact,
        params string?[] identifiers)
    {
        var info = buildInfo ?? BuildInfo.FromEntryAssembly();
        var thread = Thread.CurrentThread;
        return new CrashReportDocument
        {
            SchemaVersion = SchemaVersions.CrashReport,
            DossierId = Guid.NewGuid().ToString("N")[..8],
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IsFatal = isFatal,
            Source = source,
            SafeStopOutcome = safeStop,
            AppVersion = info.Version,
            AppCommitSha = info.CommitSha,
            InformationalVersion = info.InformationalVersion,
            RuntimeVersion = info.RuntimeVersion,
            RuntimeIdentifier = info.RuntimeIdentifier,
            OsDescription = info.OsDescription,
            ProcessArchitecture = info.ProcessArchitecture,
            Culture = System.Globalization.CultureInfo.CurrentCulture.Name,
            UptimeSeconds = Math.Max(0, (DateTimeOffset.UtcNow - info.ProcessStartUtc).TotalSeconds),
            FaultingThreadId = thread.ManagedThreadId,
            FaultingThreadName = thread.Name,
            ActiveRunId = activeRunId,
            ActivePlanId = activePlanId,
            Exceptions = CaptureExceptionChain(exception, redact, identifiers),
        };
    }

    public static string CaptureLogTail(int maxChars = DefaultMaxLogTailChars)
        => RingBufferSink.Shared?.DrainText(maxChars) ?? string.Empty;

    private static void TruncateExceptions(CrashReportDocument report)
    {
        foreach (var frame in report.Exceptions)
        {
            frame.StackTrace = Truncate(frame.StackTrace, DefaultMaxStackChars);
            frame.Message = Truncate(frame.Message, 8 * 1024) ?? string.Empty;
        }
    }

    private static string? Truncate(string? value, int maxChars)
    {
        if (value is null || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + "…(truncated)";
    }

    private static void TryWriteText(string path, string? content)
    {
        try
        {
            File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
        }
        catch
        {
            // degrade
        }
    }

    private static void TryWriteJson<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, value, typeInfo);
        }
        catch
        {
            // degrade
        }
    }
}
