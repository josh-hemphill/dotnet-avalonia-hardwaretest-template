using System.Text.Json;
using HardwareTest.Core.Serialization;

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
    public required DateTimeOffset StartedAt { get; init; }
    public required RunResult Result { get; init; }
    public string? DutSerial { get; init; }
    public string? DutPartNumber { get; init; }
    public string? SessionId { get; init; }
    public string? OperatorName { get; init; }
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
        var dir = Path.Combine(_runsDirectory, Sanitize(runId));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        var dir = GetRunDirectory(run.RunId);
        var path = Path.Combine(dir, "run.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, run, AppJsonContext.Default.TestRunRecord, cancellationToken);
    }

    public async Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetRunDirectory(runId), "run.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.TestRunRecord, cancellationToken);
    }

    public async Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_runsDirectory);
        var results = new List<TestRunSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_runsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(dir, "run.json");
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = File.OpenRead(path);
            var run = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.TestRunRecord, cancellationToken);
            if (run is null)
            {
                continue;
            }

            results.Add(new TestRunSummary
            {
                RunId = run.RunId,
                PlanName = run.PlanName,
                StartedAt = run.StartedAt,
                Result = run.Result,
                DutSerial = run.DutSerial,
                DutPartNumber = run.DutPartNumber,
                SessionId = run.SessionId,
                OperatorName = run.OperatorName,
            });
        }

        return results
            .OrderByDescending(r => r.StartedAt)
            .ToArray();
    }

    private static string Sanitize(string runId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            runId = runId.Replace(c, '_');
        }

        return runId;
    }
}

/// Placeholder for future run-to-run comparison.
public interface IRunComparisonService
{
    // TODO: Implement run-to-run comparison between persisted test runs.
    Task<string> CompareAsync(string leftRunId, string rightRunId, CancellationToken cancellationToken = default);
}

public sealed class StubRunComparisonService : IRunComparisonService
{
    public Task<string> CompareAsync(string leftRunId, string rightRunId, CancellationToken cancellationToken = default)
    {
        // TODO: Implement run-to-run comparison between persisted test runs.
        return Task.FromResult($"Comparison of {leftRunId} vs {rightRunId} is not implemented yet.");
    }
}
