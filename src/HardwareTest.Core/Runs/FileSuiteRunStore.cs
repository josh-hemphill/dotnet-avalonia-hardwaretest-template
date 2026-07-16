using System.Text.Json;
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
        var dir = Path.Combine(_runsDirectory, "suites", Sanitize(suiteRunId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task SaveAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
        var dir = GetSuiteRunDirectory(suiteRun.SuiteRunId);
        foreach (var planRun in suiteRun.PlanRuns)
        {
            await _runStore.SaveAsync(planRun, cancellationToken).ConfigureAwait(false);
        }

        var path = Path.Combine(dir, "suite-run.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, suiteRun, AppJsonContext.Default.SuiteRunRecord, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SuiteRunRecord?> LoadAsync(string suiteRunId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetSuiteRunDirectory(suiteRunId), "suite-run.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.SuiteRunRecord, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Sanitize(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(c, '_');
        }

        return id;
    }
}
