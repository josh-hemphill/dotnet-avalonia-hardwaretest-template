using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Serilog;

namespace HardwareTest.Core.Storage;

public sealed class RunRetentionResult
{
    public int DeletedCount { get; init; }
    public IReadOnlyList<string> DeletedPaths { get; init; } = [];
    public IReadOnlyList<string> SkippedInProgress { get; init; } = [];
}

public interface IRunRetentionService
{
    /// Prune completed run folders by age then count. Never deletes in-progress runs.
    RunRetentionResult Prune(bool dryRun = false);
}

/// Age + count retention for runs/ (and suites/).
public sealed class RunRetentionService : IRunRetentionService
{
    private readonly AppSettings _settings;
    private readonly string _runsDirectory;
    private readonly ILogger? _log;
    private readonly Func<DateTimeOffset> _utcNow;

    public RunRetentionService(
        AppSettings settings,
        string runsDirectory,
        ILogger? log = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _settings = settings;
        _runsDirectory = runsDirectory;
        _log = log;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public RunRetentionResult Prune(bool dryRun = false)
    {
        Directory.CreateDirectory(_runsDirectory);
        var candidates = EnumerateRunFolders()
            .Select(Analyze)
            .Where(c => c is not null)
            .Cast<RunFolderInfo>()
            .OrderByDescending(c => c.StartedAt)
            .ToList();

        var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();
        var now = _utcNow();
        var maxAgeDays = Math.Max(0, _settings.RunRetentionDays);
        var maxCount = Math.Max(0, _settings.RunRetentionMaxRuns);

        foreach (var folder in candidates)
        {
            if (folder.IsInProgress)
            {
                skipped.Add(folder.DirectoryPath);
                continue;
            }

            if (maxAgeDays > 0 && folder.StartedAt < now.AddDays(-maxAgeDays))
            {
                toDelete.Add(folder.DirectoryPath);
            }
        }

        if (maxCount > 0)
        {
            var keepable = candidates
                .Where(c => !c.IsInProgress && !toDelete.Contains(c.DirectoryPath))
                .OrderByDescending(c => c.StartedAt)
                .ToList();
            foreach (var excess in keepable.Skip(maxCount))
            {
                toDelete.Add(excess.DirectoryPath);
            }
        }

        var deleted = new List<string>();
        foreach (var path in toDelete.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (dryRun)
            {
                deleted.Add(path);
                continue;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    deleted.Add(path);
                    _log?.Information("Run retention deleted {Path}", path);
                }
            }
            catch (Exception ex)
            {
                _log?.Warning(ex, "Run retention failed to delete {Path}", path);
            }
        }

        return new RunRetentionResult
        {
            DeletedCount = deleted.Count,
            DeletedPaths = deleted,
            SkippedInProgress = skipped,
        };
    }

    private IEnumerable<string> EnumerateRunFolders()
    {
        if (!Directory.Exists(_runsDirectory))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(_runsDirectory))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, "suites", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return dir;
        }

        var suitesRoot = Path.Combine(_runsDirectory, "suites");
        if (!Directory.Exists(suitesRoot))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(suitesRoot))
        {
            yield return dir;
        }
    }

    private static RunFolderInfo? Analyze(string directoryPath)
    {
        var runJson = Path.Combine(directoryPath, "run.json");
        var suiteJson = Path.Combine(directoryPath, "suite-run.json");
        try
        {
            if (File.Exists(runJson))
            {
                using var stream = File.OpenRead(runJson);
                var run = JsonSerializer.Deserialize(stream, AppJsonContext.Default.TestRunRecord);
                if (run is null)
                {
                    return FromDirectoryTimes(directoryPath);
                }

                return new RunFolderInfo
                {
                    DirectoryPath = directoryPath,
                    StartedAt = run.StartedAt == default ? File.GetCreationTimeUtc(runJson) : run.StartedAt,
                    IsInProgress = IsInProgress(run.Result),
                };
            }

            if (File.Exists(suiteJson))
            {
                using var stream = File.OpenRead(suiteJson);
                var suite = JsonSerializer.Deserialize(stream, AppJsonContext.Default.SuiteRunRecord);
                if (suite is null)
                {
                    return FromDirectoryTimes(directoryPath);
                }

                return new RunFolderInfo
                {
                    DirectoryPath = directoryPath,
                    StartedAt = suite.StartedAt == default ? File.GetCreationTimeUtc(suiteJson) : suite.StartedAt,
                    IsInProgress = IsInProgress(suite.Result),
                };
            }
        }
        catch
        {
            // fall through to directory times
        }

        return FromDirectoryTimes(directoryPath);
    }

    private static RunFolderInfo FromDirectoryTimes(string directoryPath)
    {
        var created = Directory.GetCreationTimeUtc(directoryPath);
        return new RunFolderInfo
        {
            DirectoryPath = directoryPath,
            StartedAt = new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)),
            IsInProgress = false,
        };
    }

    private static bool IsInProgress(RunResult result)
        => result is RunResult.Unknown;

    private sealed class RunFolderInfo
    {
        public required string DirectoryPath { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public required bool IsInProgress { get; init; }
    }
}
