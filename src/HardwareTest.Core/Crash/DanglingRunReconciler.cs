using HardwareTest.Core.Runs;
using Serilog;

namespace HardwareTest.Core.Crash;

/// Converts incomplete run.json stubs left by a crash/power cut into Cancelled.
public sealed class DanglingRunReconciler
{
    public const string ProcessInterruptedReason = "ProcessInterrupted";

    private readonly IRunStore _runStore;

    public DanglingRunReconciler(IRunStore runStore)
    {
        _runStore = runStore;
    }

    public async Task<int> ReconcileAsync(
        string? correlatedCrashDossierId = null,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        IReadOnlyList<TestRunSummary> summaries;
        try
        {
            summaries = await _runStore.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Dangling run reconciliation could not list runs");
            return 0;
        }

        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summary.Result != RunResult.Unknown)
            {
                continue;
            }

            TestRunRecord? run;
            try
            {
                run = await _runStore.LoadAsync(summary.RunId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Skip dangling run load for {RunId}", summary.RunId);
                continue;
            }

            if (run is null || run.IsSchemaReadOnly)
            {
                continue;
            }

            if (run.Result != RunResult.Unknown || run.CompletedAt is not null)
            {
                continue;
            }

            run.Result = RunResult.Cancelled;
            run.CompletedAt = DateTimeOffset.UtcNow;
            var reason = ProcessInterruptedReason;
            if (!string.IsNullOrWhiteSpace(correlatedCrashDossierId))
            {
                reason = $"{ProcessInterruptedReason}; crash={correlatedCrashDossierId}";
            }

            run.ErrorMessage = string.IsNullOrWhiteSpace(run.ErrorMessage)
                ? reason
                : $"{run.ErrorMessage}; {reason}";

            try
            {
                await _runStore.SaveAsync(run, cancellationToken).ConfigureAwait(false);
                count++;
                Log.Information("Reconciled dangling run {RunId} as Cancelled ({Reason})", run.RunId, reason);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save reconciled run {RunId}", run.RunId);
            }
        }

        return count;
    }

    /// Picks the newest crash dossier id under crashRoot (folder name suffix after last '-').
    public static string? TryCorrelateNewestDossierId(string? crashRoot, TimeSpan maxAge)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(crashRoot) || !Directory.Exists(crashRoot))
            {
                return null;
            }

            var newest = new DirectoryInfo(crashRoot)
                .EnumerateDirectories()
                .OrderByDescending(d => d.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (newest is null)
            {
                return null;
            }

            if (DateTime.UtcNow - newest.CreationTimeUtc > maxAge
                && DateTime.UtcNow - newest.LastWriteTimeUtc > maxAge)
            {
                return null;
            }

            var name = newest.Name;
            var dash = name.LastIndexOf('-');
            return dash >= 0 && dash < name.Length - 1 ? name[(dash + 1)..] : name;
        }
        catch
        {
            return null;
        }
    }
}
