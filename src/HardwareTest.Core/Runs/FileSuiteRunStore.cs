using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;

namespace HardwareTest.Core.Runs;

public interface ISuiteRunStore
{
    Task SaveAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default);
    Task<SuiteRunRecord?> LoadAsync(string suiteRunId, CancellationToken cancellationToken = default);
    string GetSuiteRunDirectory(string suiteRunId);
}

public sealed class FileSuiteRunStore : ISuiteRunStore
{
    private readonly IRunStore _runStore;
    private readonly string _runsDirectory;

    public FileSuiteRunStore(IRunStore runStore, string runsDirectory)
    {
        _runStore = runStore;
        _runsDirectory = runsDirectory;
        Directory.CreateDirectory(_runsDirectory);
    }

    public string GetSuiteRunDirectory(string suiteRunId)
    {
        var dir = PathContainment.CombineUnderRoot(_runsDirectory, "suites", Sanitize(suiteRunId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task SaveAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
        if (suiteRun.IsSchemaReadOnly)
        {
            var futureChild = suiteRun.PlanRuns.FirstOrDefault(child => child.IsSchemaReadOnly);
            throw new SchemaReadOnlyException(futureChild is null
                ? DocumentSchemaGate.Evaluate(SchemaDocumentTypes.SuiteRunRecord,
                    suiteRun.StoredSchemaVersion > 0 ? suiteRun.StoredSchemaVersion : suiteRun.SchemaVersion, SchemaVersions.SuiteRunRecord)
                : DocumentSchemaGate.Evaluate(SchemaDocumentTypes.TestRunRecord,
                    futureChild.StoredSchemaVersion > 0 ? futureChild.StoredSchemaVersion : futureChild.SchemaVersion,
                    SchemaVersions.TestRunRecord, futureChild.AppVersion));
        }

        var dir = GetSuiteRunDirectory(suiteRun.SuiteRunId);
        using var write = await ReportRevisions.LockWriteAsync(dir, cancellationToken).ConfigureAwait(false);
        var committed = await LoadAsync(suiteRun.SuiteRunId, cancellationToken).ConfigureAwait(false);
        if (committed?.IsSchemaReadOnly == true)
        {
            var future = committed.PlanRuns.FirstOrDefault(r => r.IsSchemaReadOnly);
            throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
                future is null ? SchemaDocumentTypes.SuiteRunRecord : SchemaDocumentTypes.TestRunRecord,
                future?.StoredSchemaVersion ?? committed.StoredSchemaVersion,
                future is null ? SchemaVersions.SuiteRunRecord : SchemaVersions.TestRunRecord));
        }
        var candidate = JsonSerializer.Deserialize(JsonSerializer.Serialize(suiteRun, AppJsonContext.Default.SuiteRunRecord),
            AppJsonContext.Default.SuiteRunRecord)!;
        candidate.PlanRuns = suiteRun.PlanRuns.Select(ReportRevisions.Clone).ToList();
        if (committed is not null)
        {
            foreach (var member in committed.PlanRuns)
            {
                var incoming = candidate.PlanRuns.FirstOrDefault(r => r.RunId == member.RunId);
                if (incoming is null) candidate.PlanRuns.Add(ReportRevisions.Clone(member));
                else ReportRevisions.MergeHistory(incoming, member);
            }
        }
        using var members = await ReportRevisions.LockAllAsync(candidate.PlanRuns.Select(r => _runStore.GetRunDirectory(r.RunId)),
            cancellationToken).ConfigureAwait(false);
        var path = Path.Combine(dir, "suite-run.json");
        DocumentSchemaGate.RequireWritable(SchemaDocumentTypes.SuiteRunRecord, suiteRun.SchemaVersion, SchemaVersions.SuiteRunRecord, path);
        await CurrentDocumentFile.ValidateWriteDestinationAsync(path, AppJsonContext.Default.SuiteRunRecord,
            SchemaDocumentTypes.SuiteRunRecord, SchemaVersions.SuiteRunRecord, cancellationToken).ConfigureAwait(false);
        // Validate the whole batch before saving any child.
        foreach (var child in candidate.PlanRuns)
        {
            if (child.IsSchemaReadOnly) throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
                SchemaDocumentTypes.TestRunRecord, child.StoredSchemaVersion, SchemaVersions.TestRunRecord, child.AppVersion));
            DocumentSchemaGate.RequireWritable(SchemaDocumentTypes.TestRunRecord, child.SchemaVersion, SchemaVersions.TestRunRecord,
                Path.Combine(_runStore.GetRunDirectory(child.RunId), "run.json"), child.AppVersion);
            await CurrentDocumentFile.ValidateWriteDestinationAsync(Path.Combine(_runStore.GetRunDirectory(child.RunId), "run.json"),
                AppJsonContext.Default.TestRunRecord, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord,
                cancellationToken).ConfigureAwait(false);
            await ReportRevisions.RefreshHistoryAsync(child, _runStore, cancellationToken).ConfigureAwait(false);
        }
        foreach (var planRun in candidate.PlanRuns)
        {
            await _runStore.SaveAsync(planRun, cancellationToken).ConfigureAwait(false);
        }

        await CurrentDocumentFile.WriteAsync(
                path,
                candidate,
                AppJsonContext.Default.SuiteRunRecord,
                SchemaDocumentTypes.SuiteRunRecord, suiteRun.SchemaVersion, SchemaVersions.SuiteRunRecord, cancellationToken)
            .ConfigureAwait(false);
        foreach (var member in suiteRun.PlanRuns)
            ReportRevisions.PublishHistory(member, candidate.PlanRuns.First(r => r.RunId == member.RunId));
    }

    public async Task<SuiteRunRecord?> LoadAsync(string suiteRunId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetSuiteRunDirectory(suiteRunId), "suite-run.json");
        var (suite, status) = await CurrentDocumentFile.ReadAsync(path, AppJsonContext.Default.SuiteRunRecord,
            SchemaDocumentTypes.SuiteRunRecord, SchemaVersions.SuiteRunRecord, cancellationToken).ConfigureAwait(false);
        if (suite is null) return null;
        suite.StoredSchemaVersion = status.StoredVersion;
        suite.IsSchemaReadOnly = status.IsReadOnly;
        foreach (var child in suite.PlanRuns)
        {
            child.StoredSchemaVersion = child.SchemaVersion;
            child.IsSchemaReadOnly = child.SchemaVersion > SchemaVersions.TestRunRecord;
            suite.IsSchemaReadOnly |= child.IsSchemaReadOnly;
        }

        return suite;
    }

    private static string Sanitize(string id) => HardwareTest.Core.IO.PortableFileNames.Sanitize(id);
}
