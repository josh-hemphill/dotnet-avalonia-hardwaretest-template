using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class ReportRevisionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "report-revisions-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Legacy_migration_is_deterministic_and_preserves_files(int schema)
    {
        var store = new FileRunStore(_root);
        var run = new TestRunRecord { RunId = "legacy", SchemaVersion = schema };
        var dir = store.GetRunDirectory(run.RunId);
        var pdf = Path.Combine(dir, "issued", "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllTextAsync(pdf, "old pdf");
        var sidecar = Path.Combine(dir, "certification.attestation.json");
        await File.WriteAllTextAsync(sidecar, "old signature");
        run.Reports.Add(new RunReportArtifact { Kind = ReportKinds.Certification, Role = ReportArtifactRoles.Issued, PdfPath = pdf });
        run.Attestations.Add(new ReportAttestation { ReportKind = ReportKinds.Certification, SidecarPath = sidecar });
        var original = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        await File.WriteAllTextAsync(Path.Combine(dir, "run.json"), original);
        var loaded = (await store.LoadAsync(run.RunId))!;
        var again = (await store.LoadAsync(run.RunId))!;
        Assert.Equal(1, loaded.Reports[0].RevisionNumber);
        Assert.StartsWith("legacy-", loaded.Reports[0].RevisionId);
        Assert.Equal(loaded.Reports[0].RevisionId, again.Reports[0].RevisionId);
        Assert.Equal(loaded.Reports[0].RevisionId, loaded.Attestations[0].RevisionId);
        Assert.Equal(pdf, loaded.Reports[0].PdfPath);
        Assert.Equal(sidecar, loaded.Attestations[0].SidecarPath);
        Assert.Equal("old signature", await File.ReadAllTextAsync(sidecar));
        Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(dir, "run.json")));
    }

    [Fact]
    public async Task Two_signed_issuances_preserve_history_and_exact_snapshots()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store,
            new AppSettings { AllowPresenceInLieuOfSigning = true });
        var snapshot = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var first = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        var firstPdf = await File.ReadAllBytesAsync(first.PdfPath);
        Assert.Equal(snapshot, await File.ReadAllTextAsync(first.RunSnapshotPath!));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))), run.Attestations[0].RunJsonSha256);
        await File.WriteAllTextAsync(ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification)!, "%PDF-second");
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var second = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        Assert.Equal(2, second.RevisionNumber);
        Assert.NotEqual(first.RevisionId, second.RevisionId);
        Assert.Equal(2, run.Attestations.Count);
        Assert.Equal(firstPdf, await File.ReadAllBytesAsync(first.PdfPath));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification, first.RevisionId));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(run.RunId, ReportAttestationService.GuessRunIdFromPdfPath(second.PdfPath));
        Assert.Equal(first.PdfPath, ReportAttestationService.ResolvePrintOrExportPdfPath(run, first.PdfPath));
        File.Delete(second.PdfPath);
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(second.PdfPath, ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification, first.RevisionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_failure_or_cancellation_preserves_previous_record_and_signature(bool cancel)
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var original = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var disk = await File.ReadAllTextAsync(Path.Combine(store.GetRunDirectory(run.RunId), "run.json"));
        var priorSidecar = await File.ReadAllBytesAsync(run.Attestations[0].SidecarPath!);
        var failing = new FailingStore(store, cancel);
        var failingService = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), failing, new AppSettings());
        if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(() => failingService.AttestAsync(run, ReportKinds.Certification));
        else await Assert.ThrowsAsync<IOException>(() => failingService.AttestAsync(run, ReportKinds.Certification));
        Assert.Equal(original, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        Assert.Equal(disk, await File.ReadAllTextAsync(Path.Combine(store.GetRunDirectory(run.RunId), "run.json")));
        Assert.Equal(priorSidecar, await File.ReadAllBytesAsync(run.Attestations[0].SidecarPath!));
        Assert.Single((await store.LoadAsync(run.RunId))!.Attestations);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Cancelled_before_staging_does_not_publish_revision()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AttestAsync(run, ReportKinds.Certification,
            cancellationToken: cancellation.Token));
        Assert.Empty(run.Attestations);
        Assert.DoesNotContain(run.Reports, r => ReportArtifactRoles.IsIssued(r.Role));
    }

    [Fact]
    public async Task Concurrent_issuance_from_stale_records_keeps_both_revisions()
    {
        var store = new FileRunStore(_root);
        var first = await SeedAsync(store);
        var stale = (await store.LoadAsync(first.RunId))!;
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        var results = await Task.WhenAll(service.AttestAsync(first, ReportKinds.Certification),
            service.AttestAsync(stale, ReportKinds.Certification));
        Assert.All(results, r => Assert.True(r.Succeeded));
        var loaded = (await store.LoadAsync(first.RunId))!;
        Assert.Equal(new[] { 1, 2 }, loaded.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role))
            .Select(r => r.RevisionNumber).Order().ToArray());
        Assert.Equal(2, loaded.Attestations.Count);
        Assert.All(loaded.Attestations, a => Assert.True(service.HasValidAttestation(loaded, ReportKinds.Certification, a.RevisionId)));
    }

    [Fact]
    public async Task Snapshot_tampering_invalidates_only_that_revision()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var first = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        await File.WriteAllTextAsync(first.RunSnapshotPath!, "tampered");
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification, first.RevisionId));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Stage_failure_preserves_record()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        await File.WriteAllTextAsync(Path.Combine(store.GetRunDirectory(run.RunId), "issued"), "blocks directory");
        var before = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        await Assert.ThrowsAsync<IOException>(() => service.AttestAsync(run, ReportKinds.Certification));
        Assert.Equal(before, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        Assert.Empty((await store.LoadAsync(run.RunId))!.Attestations);
    }

    [Fact]
    public async Task Stale_ordinary_and_suite_saves_preserve_issued_history()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var stale = (await store.LoadAsync(run.RunId))!;
        var held = (await store.LoadAsync(run.RunId))!;
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        stale.ErrorMessage = "ordinary update";
        await store.SaveAsync(stale);
        Assert.Single(stale.Attestations);
        Assert.Equal("ordinary update", (await store.LoadAsync(run.RunId))!.ErrorMessage);
        var suites = new FileSuiteRunStore(store, _root);
        var suite = new SuiteRunRecord { SuiteRunId = "suite", PlanRuns = [held] };
        await suites.SaveAsync(suite);
        Assert.Single(held.Attestations);
        Assert.Single((await store.LoadAsync(run.RunId))!.Attestations);
        Assert.Single((await suites.LoadAsync(suite.SuiteRunId))!.PlanRuns[0].Attestations);
    }

    [Fact]
    public async Task Concurrent_ordinary_writes_and_issuance_preserve_committed_history()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var stale = (await store.LoadAsync(run.RunId))!;
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        await Task.WhenAll(service.AttestAsync(run, ReportKinds.Certification), store.SaveAsync(stale));
        var loaded = (await store.LoadAsync(run.RunId))!;
        Assert.Single(loaded.Attestations);
        Assert.True(service.HasValidAttestation(loaded, ReportKinds.Certification));
    }

    [Fact]
    public async Task New_revision_without_snapshot_is_invalid_and_legacy_revision_without_snapshot_remains_valid()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var artifact = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        artifact.RunSnapshotPath = null;
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
        artifact.RevisionId = "legacy-example";
        run.Attestations[0].RevisionId = artifact.RevisionId;
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Direct_failed_commit_does_not_mutate_caller_owned_shared_stamp()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var stamp = new ReportAttestation { ReportKind = ReportKinds.Certification, SidecarPath = "original" };
        run.Attestations.Add(stamp);
        var sidecar = new ReportAttestationSidecar { Attestation = stamp };
        var revisions = new FileReportRevisionStore(new FailingStore(store, false));
        await Assert.ThrowsAsync<IOException>(() => revisions.CommitAsync(run, ReportKinds.Certification, [1, 2], sidecar));
        Assert.Same(stamp, sidecar.Attestation);
        Assert.Same(stamp, run.Attestations[0]);
        Assert.Null(stamp.RevisionId);
        Assert.Equal("original", stamp.SidecarPath);
    }

    [Fact]
    public async Task Ordinary_save_failure_and_future_disk_schema_do_not_mutate_caller()
    {
        var store = new FileRunStore(_root);
        var run = await SeedAsync(store);
        var path = Path.Combine(store.GetRunDirectory(run.RunId), "run.json");
        run.SchemaVersion = 0;
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(run));
            Assert.Equal(0, run.SchemaVersion);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        var future = ReportRevisions.Clone(run);
        future.SchemaVersion = SchemaVersions.TestRunRecord + 1;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(future, AppJsonContext.Default.TestRunRecord));
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(run));
        Assert.Equal(0, run.SchemaVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Suite_embedded_legacy_reports_migrate_in_memory_and_keep_valid_signatures(int schema)
    {
        var runs = new FileRunStore(_root);
        var suites = new FileSuiteRunStore(runs, _root);
        var run = await SeedAsync(runs);
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), runs, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var artifact = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        var stamp = run.Attestations[0];
        var pdfBytes = await File.ReadAllBytesAsync(artifact.PdfPath);
        var sidecarBytes = await File.ReadAllBytesAsync(stamp.SidecarPath!);
        artifact.RevisionId = null;
        artifact.RevisionNumber = 0;
        artifact.RunSnapshotPath = null;
        stamp.RevisionId = null;
        run.SchemaVersion = schema;
        var suite = new SuiteRunRecord { SuiteRunId = "legacy-suite", PlanRuns = [run] };
        var path = Path.Combine(suites.GetSuiteRunDirectory(suite.SuiteRunId), "suite-run.json");
        var original = JsonSerializer.Serialize(suite, AppJsonContext.Default.SuiteRunRecord);
        await File.WriteAllTextAsync(path, original);
        var loaded = (await suites.LoadAsync(suite.SuiteRunId))!;
        var member = Assert.Single(loaded.PlanRuns);
        Assert.Equal(schema, member.StoredSchemaVersion);
        Assert.Equal(schema == 0, member.IsLegacy);
        Assert.False(member.IsSchemaReadOnly);
        var issued = ReportRevisions.Latest(member, ReportKinds.Certification)!;
        Assert.StartsWith("legacy-", issued.RevisionId);
        Assert.Equal(1, issued.RevisionNumber);
        Assert.Equal(issued.RevisionId, member.Attestations[0].RevisionId);
        Assert.True(service.HasValidAttestation(member, ReportKinds.Certification));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Equal(pdfBytes, await File.ReadAllBytesAsync(issued.PdfPath));
        Assert.Equal(sidecarBytes, await File.ReadAllBytesAsync(member.Attestations[0].SidecarPath!));
    }

    [Fact]
    public async Task Suite_future_member_is_readonly_and_refuses_save_without_downgrading()
    {
        var runs = new FileRunStore(_root);
        var suites = new FileSuiteRunStore(runs, _root);
        var future = new TestRunRecord
        {
            RunId = "future-member",
            SchemaVersion = SchemaVersions.TestRunRecord + 1,
            Reports = [new() { Kind = ReportKinds.Certification, Role = ReportArtifactRoles.Issued, PdfPath = "unchanged.pdf" }],
        };
        var suite = new SuiteRunRecord { SuiteRunId = "future-suite", PlanRuns = [future] };
        var path = Path.Combine(suites.GetSuiteRunDirectory(suite.SuiteRunId), "suite-run.json");
        var original = JsonSerializer.Serialize(suite, AppJsonContext.Default.SuiteRunRecord);
        await File.WriteAllTextAsync(path, original);
        var loaded = (await suites.LoadAsync(suite.SuiteRunId))!;
        var member = Assert.Single(loaded.PlanRuns);
        Assert.True(member.IsSchemaReadOnly);
        Assert.Equal(future.SchemaVersion, member.SchemaVersion);
        Assert.Equal(future.SchemaVersion, member.StoredSchemaVersion);
        Assert.Null(member.Reports[0].RevisionId);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => suites.SaveAsync(loaded));
        Assert.Equal(future.SchemaVersion, member.SchemaVersion);
        Assert.Null(member.Reports[0].RevisionId);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(Path.Combine(runs.GetRunDirectory(member.RunId), "run.json")));
    }

    private static async Task<TestRunRecord> SeedAsync(FileRunStore store)
    {
        var run = new TestRunRecord { RunId = "run-revisions" };
        var path = Path.Combine(store.GetRunDirectory(run.RunId), "certification.pdf");
        await File.WriteAllTextAsync(path, "%PDF-first");
        run.Reports.Add(new RunReportArtifact { Kind = ReportKinds.Certification, PdfPath = path });
        await store.SaveAsync(run);
        return run;
    }

    private sealed class FailingStore(IRunStore inner, bool cancel) : IRunStore
    {
        public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
            => cancel ? Task.FromException(new OperationCanceledException()) : Task.FromException(new IOException("Injected save failure"));
        public Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default) => inner.LoadAsync(runId, cancellationToken);
        public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public string GetRunDirectory(string runId) => inner.GetRunDirectory(runId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
