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
        if (suiteRun.IsSchemaReadOnly || suiteRun.SchemaVersion > SchemaVersions.SuiteRunRecord)
        {
            throw new SchemaReadOnlyException(
                DocumentSchemaGate.Evaluate(
                    SchemaDocumentTypes.SuiteRunRecord,
                    suiteRun.StoredSchemaVersion > 0 ? suiteRun.StoredSchemaVersion : suiteRun.SchemaVersion,
                    SchemaVersions.SuiteRunRecord));
        }

        var dir = GetSuiteRunDirectory(suiteRun.SuiteRunId);
        using var write = await ReportRevisions.LockWriteAsync(dir, cancellationToken).ConfigureAwait(false);
        var committed = await LoadAsync(suiteRun.SuiteRunId, cancellationToken).ConfigureAwait(false);
        if (committed?.IsSchemaReadOnly == true) throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
            SchemaDocumentTypes.SuiteRunRecord, committed.StoredSchemaVersion, SchemaVersions.SuiteRunRecord));
        var candidate = JsonSerializer.Deserialize(JsonSerializer.Serialize(suiteRun, AppJsonContext.Default.SuiteRunRecord),
            AppJsonContext.Default.SuiteRunRecord)!;
        candidate.SchemaVersion = SchemaVersions.SuiteRunRecord;
        candidate.PlanRuns = suiteRun.PlanRuns.Select(ReportRevisions.Clone).ToList();
        foreach (var planRun in candidate.PlanRuns)
        {
            await _runStore.SaveAsync(planRun, cancellationToken).ConfigureAwait(false);
        }

        var path = Path.Combine(dir, "suite-run.json");
        await AtomicFile.WriteJsonAsync(
                path,
                candidate,
                AppJsonContext.Default.SuiteRunRecord,
                cancellationToken)
            .ConfigureAwait(false);
        suiteRun.SchemaVersion = candidate.SchemaVersion;
        for (var index = 0; index < suiteRun.PlanRuns.Count; index++)
            ReportRevisions.PublishHistory(suiteRun.PlanRuns[index], candidate.PlanRuns[index]);
    }

    public async Task<SuiteRunRecord?> LoadAsync(string suiteRunId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetSuiteRunDirectory(suiteRunId), "suite-run.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var suite = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.SuiteRunRecord, cancellationToken)
            .ConfigureAwait(false);
        if (suite is null)
        {
            return null;
        }

        var status = DocumentSchemaGate.Apply(
            SchemaDocumentTypes.SuiteRunRecord,
            suite.SchemaVersion,
            SchemaVersions.SuiteRunRecord,
            path,
            document: suite);
        suite.StoredSchemaVersion = status.StoredVersion;
        suite.IsLegacy = status.IsLegacy;
        suite.IsSchemaReadOnly = status.IsReadOnly;
        if (status.Kind is DocumentSchemaKind.Current or DocumentSchemaKind.UpgradeNeeded)
        {
            suite.SchemaVersion = SchemaVersions.SuiteRunRecord;
        }

        return suite;
    }

    private static string Sanitize(string id) => HardwareTest.Core.IO.PortableFileNames.Sanitize(id);
}
