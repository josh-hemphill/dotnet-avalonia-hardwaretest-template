using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.IO;
using HardwareTest.Core.Serialization;

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

    public static TestRunRecord Clone(TestRunRecord run)
    {
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord),
            AppJsonContext.Default.TestRunRecord)!;
        copy.IsSchemaReadOnly = run.IsSchemaReadOnly;
        copy.StoredSchemaVersion = run.StoredSchemaVersion;
        copy.IsLegacy = run.IsLegacy;
        return copy;
    }

    public static void MigrateLegacy(TestRunRecord run)
    {
        foreach (var artifact in run.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role) && string.IsNullOrEmpty(r.RevisionId)))
        {
            artifact.RevisionId = "legacy-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{run.RunId}:{artifact.Kind.ToLowerInvariant()}:{artifact.PdfPath}"))).ToLowerInvariant()[..24];
            artifact.RevisionNumber = 1;
            foreach (var attestation in run.Attestations.Where(a => string.Equals(a.ReportKind, artifact.Kind,
                         StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(a.RevisionId)))
                attestation.RevisionId = artifact.RevisionId;
        }
    }

    public static async Task RefreshHistoryAsync(TestRunRecord run, IRunStore store, CancellationToken cancellationToken)
    {
        var committed = await store.LoadAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        MigrateLegacy(run);
        if (committed is null) return;
        if (committed.IsSchemaReadOnly) throw new SchemaReadOnlyException(DocumentSchemaGate.Evaluate(
            SchemaDocumentTypes.TestRunRecord, committed.StoredSchemaVersion, SchemaVersions.TestRunRecord, committed.AppVersion));
        var issued = committed.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)).ToList();
        issued.AddRange(run.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)
            && !issued.Any(c => c.RevisionId == r.RevisionId && string.Equals(c.Kind, r.Kind, StringComparison.OrdinalIgnoreCase))));
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
        .Where(r => ReportArtifactRoles.IsIssued(r.Role) && string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(r => r.RevisionNumber).FirstOrDefault();
}

public interface IReportRevisionStore
{
    Task CommitAsync(TestRunRecord run, string kind, byte[] pdf, ReportAttestationSidecar sidecar,
        CancellationToken cancellationToken = default, bool operationLockHeld = false);
}

/// Publishes a revision by saving its files first and the authoritative run record last.
/// Signing callers hold the shared per-run lock across compile/sign/commit and pass operationLockHeld.
/// Direct commit callers acquire that lock here.
public sealed class FileReportRevisionStore(IRunStore runs) : IReportRevisionStore
{
    public async Task CommitAsync(TestRunRecord run, string kind, byte[] pdf, ReportAttestationSidecar sidecar,
        CancellationToken cancellationToken = default, bool operationLockHeld = false)
    {
        using var operation = operationLockHeld ? null : await ReportRevisions.LockAsync(runs.GetRunDirectory(run.RunId), cancellationToken).ConfigureAwait(false);
        if (PortableFileNames.Sanitize(kind) != kind || kind is "." or "..")
            throw new ArgumentException("Report kind must be a portable filename.", nameof(kind));
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
        document.SidecarPath = Path.Combine(destination, kind + ".attestation.json");
        var artifact = new RunReportArtifact
        {
            Kind = kind, Title = ReportKinds.Title(kind), Role = ReportArtifactRoles.Issued,
            RevisionId = id, RevisionNumber = (ReportRevisions.Latest(candidate, kind)?.RevisionNumber ?? 0) + 1,
            PdfPath = Path.Combine(destination, kind + ".pdf"), GeneratedAt = document.CapturedAt,
            RunSnapshotPath = Path.Combine(destination, "run.snapshot.json"),
        };
        candidate.Reports.Add(artifact);
        candidate.Attestations.Add(document);
        if (string.IsNullOrWhiteSpace(candidate.OperatorName)) candidate.OperatorName = document.DisplayName;
        Directory.CreateDirectory(stage);
        try
        {
            await AtomicFile.WriteAllBytesAsync(Path.Combine(stage, kind + ".pdf"), pdf, cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteAllTextAsync(Path.Combine(stage, "run.snapshot.json"), snapshot, cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteJsonAsync(Path.Combine(stage, kind + ".attestation.json"), sidecar,
                AppJsonContext.Default.ReportAttestationSidecar, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
            await runs.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            run.Reports = candidate.Reports;
            run.Attestations = candidate.Attestations;
            run.OperatorName = candidate.OperatorName;
            run.SchemaVersion = candidate.SchemaVersion;
        }
        finally
        {
            // A renamed directory is harmless if the record save failed: only referenced revisions are visible.
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }
}
