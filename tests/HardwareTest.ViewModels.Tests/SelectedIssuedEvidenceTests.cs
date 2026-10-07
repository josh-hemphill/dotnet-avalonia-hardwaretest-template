using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class SelectedIssuedEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "selected-issued-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Results_print_validates_the_selected_issue_independently_of_latest(bool unrevisioned, bool tamperOld)
    {
        var (store, run, service, old, latest) = await CreateIssuesAsync(unrevisioned);
        await File.AppendAllTextAsync(tamperOld ? old.PdfPath : latest.PdfPath, "tampered");
        Assert.Equal(tamperOld, service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(!tamperOld, service.HasValidAttestationForPdf(run, ReportKinds.Certification, old.PdfPath));
        var vm = new ResultsViewModel(store, new FakeReportService(), attestation: service);
        string? printed = null;
        vm.CertifiedPrintReady += (_, path) => printed = path;

        await vm.RequestCertifiedPrintAsync(old.PdfPath);

        Assert.Equal(tamperOld ? null : old.PdfPath, printed);
        Assert.False(vm.ShowAttestationPrompt);
        if (tamperOld) Assert.Contains("Verification failed", vm.Status, StringComparison.Ordinal);
        else
        {
            // A previous successful historical print cannot authorize exporting the invalid latest issue.
            await vm.ExportPackageCommand.ExecuteAsync();
            Assert.True(vm.ShowAttestationPrompt);
        }
        Assert.Equal(2, (await store.LoadAsync(run.RunId))!.Reports.Count(r => ReportArtifactRoles.IsIssued(r.Role)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_blocks_a_tampered_selected_issue_even_when_latest_verifies(bool unrevisioned)
    {
        var (store, run, service, old, _) = await CreateIssuesAsync(unrevisioned);
        await File.AppendAllTextAsync(old.PdfPath, "tampered");
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), attestation: service)
        { UiScheduler = action => action() };
        string? blocked = null;
        vm.CertificationRequiredForPrint += (_, path) => blocked = path;
        await vm.LoadFromPathAsync(old.PdfPath);

        await vm.PrintCommand.ExecuteAsync();

        Assert.Equal(old.PdfPath, blocked);
        Assert.Equal(old.PdfPath, vm.PdfPath);
    }

    [Fact]
    public async Task Case_distinct_unix_issue_does_not_borrow_valid_counterpart_evidence()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(ReportAttestationService.PathEquals("issued/report.pdf", "ISSUED/REPORT.PDF"));
            return;
        }
        if (!OperatingSystem.IsLinux()) return;
        var (store, run, service, old, _) = await CreateIssuesAsync(unrevisioned: true);
        var upper = Path.Combine(Path.GetDirectoryName(old.PdfPath)!, Path.GetFileNameWithoutExtension(old.PdfPath).ToUpperInvariant() + ".pdf");
        var upperSidecar = Path.ChangeExtension(upper, ".attestation.json");
        Assert.False(ReportAttestationService.PathEquals(old.PdfPath, upper));
        File.Copy(old.PdfPath, upper);
        var original = ReportAttestationService.FindForArtifact(run, old)!;
        var sidecar = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(original.SidecarPath!), AppJsonContext.Default.ReportAttestationSidecar)!;
        sidecar.Attestation.SidecarPath = upperSidecar;
        await File.WriteAllTextAsync(upperSidecar, JsonSerializer.Serialize(sidecar, AppJsonContext.Default.ReportAttestationSidecar));
        run.Reports.Add(new() { Kind = old.Kind, Role = ReportArtifactRoles.Issued, PdfPath = upper, GeneratedAt = old.GeneratedAt });
        run.Attestations.Add(sidecar.Attestation);
        await File.WriteAllTextAsync(Path.Combine(store.GetRunDirectory(run.RunId), "run.json"), JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        Assert.True(service.HasValidAttestationForPdf(run, ReportKinds.Certification, upper));
        await File.AppendAllTextAsync(upper, "tampered distinct file");
        Assert.True(service.HasValidAttestationForPdf(run, ReportKinds.Certification, old.PdfPath));
        Assert.False(service.HasValidAttestationForPdf(run, ReportKinds.Certification, upper));
        var exports = ResultsViewModel.CollectExportReportFiles(run).ToArray();
        Assert.Contains(exports, entry => entry.SourcePath == old.PdfPath && entry.RelativeName.StartsWith("history", StringComparison.Ordinal));
        Assert.Contains(exports, entry => entry.SourcePath == upper && entry.RelativeName.StartsWith("history", StringComparison.Ordinal));
        Assert.Equal(exports.Length, exports.Select(entry => entry.RelativeName).Distinct(StringComparer.Ordinal).Count());
        var results = new ResultsViewModel(store, new FakeReportService(), attestation: service);
        string? printed = null;
        results.CertifiedPrintReady += (_, path) => printed = path;
        await results.RequestCertifiedPrintAsync(upper);
        Assert.Null(printed);
        Assert.Contains("Verification failed", results.Status, StringComparison.Ordinal);
        await results.RequestCertifiedPrintAsync(old.PdfPath);
        Assert.Equal(old.PdfPath, printed);
        Assert.False(results.ShowAttestationPrompt);
        var preview = new ReportPreviewViewModel(store, new FakeReportService(), attestation: service) { UiScheduler = action => action() };
        string? blocked = null;
        preview.CertificationRequiredForPrint += (_, path) => blocked = path;
        await preview.LoadFromPathAsync(upper);
        await preview.PrintCommand.ExecuteAsync();
        Assert.Equal(upper, blocked);
        Assert.Equal(upper, preview.PdfPath);
    }

    private async Task<(FileRunStore, TestRunRecord, ReportAttestationService, RunReportArtifact, RunReportArtifact)> CreateIssuesAsync(bool unrevisioned)
    {
        var store = new FileRunStore(_root);
        var run = new TestRunRecord { RunId = "selected-evidence" };
        var working = Path.Combine(store.GetRunDirectory(run.RunId), "certification.pdf");
        await File.WriteAllTextAsync(working, "%PDF-mock-evidence");
        run.Reports = [new() { Kind = ReportKinds.Certification, PdfPath = working }];
        await store.SaveAsync(run);
        var settings = new AppSettings { RequireAttestationBeforeExport = true };
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(canSign: true), store, settings);
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var oldRevisionId = ReportRevisions.Latest(run, ReportKinds.Certification)!.RevisionId;
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var old = run.Reports.Single(r => r.RevisionId == oldRevisionId);
        var latest = ReportRevisions.Latest(run, ReportKinds.Certification)!;
        if (unrevisioned)
        {
            // Current schema4 also permits artifacts predating the optional revision metadata.
            foreach (var artifact in run.Reports.Where(r => ReportArtifactRoles.IsIssued(r.Role)))
            {
                var stamp = run.Attestations.Single(a => a.RevisionId == artifact.RevisionId);
                var sidecar = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(stamp.SidecarPath!),
                    AppJsonContext.Default.ReportAttestationSidecar)!;
                artifact.RevisionId = stamp.RevisionId = sidecar.Attestation.RevisionId = null;
                artifact.RevisionNumber = 0;
                artifact.RunSnapshotPath = artifact.SidecarSha256 = null;
                await File.WriteAllTextAsync(stamp.SidecarPath!, JsonSerializer.Serialize(sidecar, AppJsonContext.Default.ReportAttestationSidecar));
            }
            old.GeneratedAt = DateTimeOffset.UnixEpoch;
            latest.GeneratedAt = DateTimeOffset.UnixEpoch.AddSeconds(1);
            await File.WriteAllTextAsync(Path.Combine(store.GetRunDirectory(run.RunId), "run.json"),
                JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        }
        Assert.True(service.HasValidAttestationForPdf(run, ReportKinds.Certification, old.PdfPath));
        Assert.True(service.HasValidAttestationForPdf(run, ReportKinds.Certification, latest.PdfPath));
        Assert.False(service.HasValidAttestationForPdf(run, ReportKinds.Status, old.PdfPath));
        Assert.False(service.HasValidAttestationForPdf(run, ReportKinds.Certification, working));
        return (store, run, service, old, latest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
