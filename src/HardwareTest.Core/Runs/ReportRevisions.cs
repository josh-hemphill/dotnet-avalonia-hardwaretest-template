using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Runs;

public static class ReportRevisions
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IDisposable> LockAsync(string runDirectory, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(runDirectory), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    public static async Task<IDisposable> LockWriteAsync(string runDirectory, CancellationToken cancellationToken)
    {
        var gate = WriteGates.GetOrAdd(Path.GetFullPath(runDirectory), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    internal static async Task<IDisposable> LockAllAsync(IEnumerable<string> directories, CancellationToken cancellationToken)
    {
        var leases = new List<IDisposable>();
        try
        {
            foreach (var directory in directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
                leases.Add(await LockAsync(directory, cancellationToken).ConfigureAwait(false));
            return new CompositeLease(leases);
        }
        catch
        {
            new CompositeLease(leases).Dispose();
            throw;
        }
    }

    private sealed class CompositeLease(List<IDisposable> leases) : IDisposable
    {
        public void Dispose()
        {
            for (var index = leases.Count - 1; index >= 0; index--) leases[index].Dispose();
        }
    }

    public static TestRunRecord Clone(TestRunRecord run)
    {
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord),
            AppJsonContext.Default.TestRunRecord)!;
        copy.IsSchemaReadOnly = run.IsSchemaReadOnly;
        copy.StoredSchemaVersion = run.StoredSchemaVersion;
        return copy;
    }

    public static async Task RefreshHistoryAsync(TestRunRecord run, IRunStore store, CancellationToken cancellationToken)
    {
        var committed = await store.LoadAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        if (committed is null) return;
        MergeHistory(run, committed);
    }

    internal static void MergeHistory(TestRunRecord run, TestRunRecord committed)
    {
        if (committed.IsSchemaReadOnly) throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
            SchemaDocumentTypes.TestRunRecord, committed.StoredSchemaVersion, SchemaVersions.TestRunRecord, committed.AppVersion));
        var issued = committed.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)).ToList();
        issued.AddRange(run.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)
            && !issued.Any(c => c.RevisionId == r.RevisionId && (r.RevisionId is not null || c.PdfPath == r.PdfPath)
                && string.Equals(c.Kind, r.Kind, StringComparison.OrdinalIgnoreCase))));
        run.Reports = run.Reports.Where(r => !ReportArtifactRoles.IsIssued(r.Role)).Concat(issued).ToList();
        var stamps = committed.Attestations.ToList();
        stamps.AddRange(run.Attestations.Where(a => !stamps.Any(c => c.RevisionId == a.RevisionId
            && string.Equals(c.ReportKind, a.ReportKind, StringComparison.OrdinalIgnoreCase)
            && (a.RevisionId is not null || c.SidecarPath == a.SidecarPath))));
        run.Attestations = stamps;
    }

    public static void PublishHistory(TestRunRecord destination, TestRunRecord source)
    {
        destination.Reports = source.Reports;
        destination.Attestations = source.Attestations;
        destination.OperatorName = source.OperatorName;
        destination.SchemaVersion = source.SchemaVersion;
    }

    public static RunReportArtifact? Latest(TestRunRecord run, string kind) => run.Reports
        .Select((artifact, index) => (artifact, index))
        .Where(item => ReportArtifactRoles.IsIssued(item.artifact.Role)
            && string.Equals(item.artifact.Kind, kind, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(item => item.artifact.RevisionNumber)
        .ThenByDescending(item => item.artifact.GeneratedAt)
        .ThenByDescending(item => item.index)
        .Select(item => item.artifact).FirstOrDefault();

}

public interface IReportRevisionStore
{
    Task CommitAsync(TestRunRecord run, string kind, byte[] pdf, ReportAttestationSidecar sidecar,
        CancellationToken cancellationToken = default, bool operationLockHeld = false);
}

/// Publishes a revision by saving its files first and the authoritative run record last.
/// Signing callers hold the shared per-run lock across compile/sign/commit and pass operationLockHeld.
/// Direct commit callers acquire that lock here.
public sealed class FileReportRevisionStore(IRunStore runs, IClock? clock = null) : IReportRevisionStore
{
    public async Task CommitAsync(TestRunRecord run, string kind, byte[] pdf, ReportAttestationSidecar sidecar,
        CancellationToken cancellationToken = default, bool operationLockHeld = false)
    {
        using var operation = operationLockHeld ? null : await ReportRevisions.LockAsync(runs.GetRunDirectory(run.RunId), cancellationToken).ConfigureAwait(false);
        if (PortableFileNames.Sanitize(kind) != kind || kind is "." or "..")
            throw new ArgumentException("Report kind must be a portable filename.", nameof(kind));
        ReportAttestationService.RequireReportWritable(run, runs.GetRunDirectory(run.RunId));
        var candidate = ReportRevisions.Clone(run);
        await ReportRevisions.RefreshHistoryAsync(candidate, runs, cancellationToken).ConfigureAwait(false);
        var snapshot = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var id = Guid.NewGuid().ToString("N");
        var parent = PathContainment.CombineUnderRoot(runs.GetRunDirectory(run.RunId), "issued", kind);
        var destination = Path.Combine(parent, id);
        var stage = Path.Combine(parent, ".staging-" + id);
        sidecar = JsonSerializer.Deserialize(JsonSerializer.Serialize(sidecar, AppJsonContext.Default.ReportAttestationSidecar),
            AppJsonContext.Default.ReportAttestationSidecar)!;
        var document = sidecar.Attestation;
        document.RevisionId = id;
        document.SidecarPath = Path.Combine(destination, kind + "-" + id + ".attestation.json");
        var artifact = new RunReportArtifact
        {
            Kind = kind,
            Title = ReportKinds.Title(kind),
            Role = ReportArtifactRoles.Issued,
            RevisionId = id,
            RevisionNumber = candidate.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)
                && string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)).Select(r => r.RevisionNumber).DefaultIfEmpty().Max() + 1,
            PdfPath = Path.Combine(destination, kind + "-" + id + ".pdf"),
            GeneratedAt = (clock ?? SystemClock.Instance).UtcNow,
            RunSnapshotPath = Path.Combine(destination, "run.snapshot.json"),
        };
        candidate.Reports.Add(artifact);
        candidate.Attestations.Add(document);
        if (string.IsNullOrWhiteSpace(candidate.OperatorName)) candidate.OperatorName = document.DisplayName;
        Directory.CreateDirectory(stage);
        try
        {
            await AtomicFile.WriteAllBytesAsync(Path.Combine(stage, kind + "-" + id + ".pdf"), pdf, cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteAllTextAsync(Path.Combine(stage, "run.snapshot.json"), snapshot, cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteJsonAsync(Path.Combine(stage, kind + "-" + id + ".attestation.json"), sidecar,
                AppJsonContext.Default.ReportAttestationSidecar, cancellationToken).ConfigureAwait(false);
            artifact.SidecarSha256 = Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(Path.Combine(stage, kind + "-" + id + ".attestation.json"), cancellationToken).ConfigureAwait(false)));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
            await runs.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            run.Reports = candidate.Reports;
            run.Attestations = candidate.Attestations;
            run.OperatorName = candidate.OperatorName;
            run.SchemaVersion = candidate.SchemaVersion;
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
        finally
        {
            // Staging files never become visible before the authoritative run record references them.
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }
}
