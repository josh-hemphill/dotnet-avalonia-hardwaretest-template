using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using Serilog;

namespace HardwareTest.Core.Runs;

public interface IRunStore
{
    Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default);
    Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default);
    string GetRunDirectory(string runId);
}

public sealed class TestRunSummary
{
    public required string RunId { get; init; }
    public required string PlanName { get; init; }
    public string PlanId { get; init; } = string.Empty;
    public required DateTimeOffset StartedAt { get; init; }
    public required RunResult Result { get; init; }
    public string? DutSerial { get; init; }
    public string? DutPartNumber { get; init; }
    public string? SessionId { get; init; }
    public string? OperatorName { get; init; }
    public bool IsSchemaReadOnly { get; init; }
    public int SchemaVersion { get; init; }
}

public sealed class FileRunStore : IRunStore
{
    private readonly string _runsDirectory;

    public FileRunStore(string runsDirectory)
    {
        _runsDirectory = runsDirectory;
        Directory.CreateDirectory(_runsDirectory);
    }

    public string GetRunDirectory(string runId)
    {
        var dir = PathContainment.CombineUnderRoot(_runsDirectory, Sanitize(runId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        if (run.IsSchemaReadOnly)
        {
            throw new SchemaReadOnlyException(
                DocumentSchemaGate.Evaluate(
                    SchemaDocumentTypes.TestRunRecord,
                    run.StoredSchemaVersion > 0 ? run.StoredSchemaVersion : run.SchemaVersion,
                    SchemaVersions.TestRunRecord,
                    run.AppVersion));
        }

        var dir = GetRunDirectory(run.RunId);
        DocumentSchemaGate.RequireWritable(SchemaDocumentTypes.TestRunRecord, run.SchemaVersion,
            SchemaVersions.TestRunRecord, Path.Combine(dir, "run.json"), run.AppVersion);
        using var write = await ReportRevisions.LockWriteAsync(dir, cancellationToken).ConfigureAwait(false);
        var candidate = ReportRevisions.Clone(run);
        await ReportRevisions.RefreshHistoryAsync(candidate, this, cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(dir, "run.json");
        await CurrentDocumentFile.WriteAsync(path, candidate, AppJsonContext.Default.TestRunRecord,
                SchemaDocumentTypes.TestRunRecord, run.SchemaVersion, SchemaVersions.TestRunRecord, cancellationToken)
            .ConfigureAwait(false);
        ReportRevisions.PublishHistory(run, candidate);
    }

    public async Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetRunDirectory(runId), "run.json");
        var (run, status) = await CurrentDocumentFile.ReadAsync(path, AppJsonContext.Default.TestRunRecord,
            SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord, cancellationToken).ConfigureAwait(false);
        if (run is null) return null;
        ApplySchemaStatus(run, status);
        return run;
    }

    public async Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_runsDirectory);
        var results = new List<TestRunSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_runsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(dir, "run.json");
            if (!File.Exists(path) && !File.Exists(path + ".bak"))
            {
                continue;
            }

            TestRunRecord? run;
            try
            {
                var loaded = await CurrentDocumentFile.ReadAsync(path, AppJsonContext.Default.TestRunRecord,
                    SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord, cancellationToken).ConfigureAwait(false);
                run = loaded.Document;
                if (run is not null) ApplySchemaStatus(run, loaded.Status);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or UnsupportedDocumentSchemaException or SchemaReadOnlyException)
            {
                Log.Warning(ex, "Skipping unreadable run record at {Path}", path);
                continue;
            }

            if (run is null)
            {
                continue;
            }

            results.Add(new TestRunSummary
            {
                RunId = run.RunId,
                PlanName = run.PlanName,
                PlanId = run.PlanId,
                StartedAt = run.StartedAt,
                Result = run.Result,
                DutSerial = run.DutSerial,
                DutPartNumber = run.DutPartNumber,
                SessionId = run.SessionId,
                OperatorName = run.OperatorName,
                IsSchemaReadOnly = run.IsSchemaReadOnly,
                SchemaVersion = run.StoredSchemaVersion,
            });
        }

        return results
            .OrderByDescending(r => r.StartedAt)
            .ToArray();
    }

    private static void ApplySchemaStatus(TestRunRecord run, DocumentSchemaStatus status)
    {
        run.StoredSchemaVersion = status.StoredVersion;
        run.IsSchemaReadOnly = status.IsReadOnly;
    }

    private static string Sanitize(string runId) => HardwareTest.Core.IO.PortableFileNames.Sanitize(runId);
}
