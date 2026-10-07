using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsExportEvidenceTests
{
    [Fact]
    public async Task Export_uses_latest_issue_evidence_when_pdf_bytes_are_identical()
    {
        using var store = new FakeRunStore();
        var run = await CreateCertificationAsync(store);
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false), store,
            new AppSettings { AllowPresenceInLieuOfSigning = true }, clock);
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var first = run.Attestations[0];
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var current = run.Attestations[1];
        Assert.Equal(first.PdfSha256, current.PdfSha256);
        Assert.NotEqual(first.SidecarPath, current.SidecarPath);
        var stray = Path.Combine(store.GetRunDirectory(run.RunId), "certification.attestation.json");
        await File.WriteAllTextAsync(stray, "unrelated evidence");

        // Append order does not select the issue: the older artifact is deliberately last.
        (run.Reports[1], run.Reports[2]) = (run.Reports[2], run.Reports[1]);
        var files = ResultsViewModel.CollectExportReportFiles(run).ToList();
        var evidence = Assert.Single(files, f => f.RelativeName == "certification.attestation.json");
        Assert.Equal(current.SidecarPath, evidence.SourcePath);
        Assert.DoesNotContain(files, f => f.SourcePath == stray);
        Assert.DoesNotContain(files, f => f.SourcePath == first.SidecarPath && f.RelativeName == "certification.attestation.json");
        Assert.Contains(files, f => f.SourcePath == first.SidecarPath && f.RelativeName.StartsWith("history", StringComparison.Ordinal));
        var export = new CapturingExportTargetService();
        var destination = export.ExportPackage(export.ListTargets()[0], "current-evidence", files);
        try
        {
            Assert.Equal(await File.ReadAllBytesAsync(current.SidecarPath!),
                await File.ReadAllBytesAsync(Path.Combine(destination, evidence.RelativeName)));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("kind")]
    [InlineData("stem")]
    public async Task Export_never_substitutes_other_issue_evidence(string rejection)
    {
        using var store = new FakeRunStore();
        var run = await CreateCertificationAsync(store);
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false), store,
            new AppSettings { AllowPresenceInLieuOfSigning = true }, clock);
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var current = run.Attestations[1];
        switch (rejection)
        {
            case "missing":
                File.Delete(current.SidecarPath!);
                break;
            case "hash":
                current.PdfSha256 = "mismatched";
                break;
            case "kind":
                current.ReportKind = ReportKinds.Status;
                break;
            case "stem":
                current.SidecarPath = run.Attestations[0].SidecarPath;
                break;
        }

        var files = ResultsViewModel.CollectExportReportFiles(run).ToList();
        Assert.Contains(files, f => f.SourcePath == run.Reports[2].PdfPath && f.RelativeName == "certification.pdf");
        Assert.DoesNotContain(files, f => f.RelativeName == "certification.attestation.json");
        Assert.DoesNotContain(files, f => f.SourcePath == current.SidecarPath
            && f.RelativeName.Contains(run.Reports[2].RevisionId!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fake_run_stores_isolate_reused_run_ids_and_dispose_only_owned_files()
    {
        using var first = new FakeRunStore();
        using var second = new FakeRunStore();
        var firstRun = await CreateCertificationAsync(first);
        var secondRun = await CreateCertificationAsync(second);
        Assert.NotEqual(first.GetRunDirectory(firstRun.RunId), second.GetRunDirectory(secondRun.RunId));

        first.Dispose();

        Assert.False(Directory.Exists(first.GetRunDirectory(firstRun.RunId)));
        Assert.True(File.Exists(secondRun.Reports[0].PdfPath));
    }

    private static async Task<TestRunRecord> CreateCertificationAsync(FakeRunStore store)
    {
        const string runId = "reused-certification";
        var pdf = Path.Combine(store.GetRunDirectory(runId), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = runId,
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Certification,
                    Role = ReportArtifactRoles.Working,
                    PdfPath = pdf,
                },
            ],
        };
        store.Seed(run);
        return run;
    }
}
