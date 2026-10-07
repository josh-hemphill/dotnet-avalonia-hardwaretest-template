using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Features.Results;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsReportHistoryTests
{
    [Fact]
    public void Package_exports_latest_and_history_while_print_preserves_explicit_issue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HardwareTestReportHistory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var working = Path.Combine(directory, "working.pdf");
            var oldIssue = Path.Combine(directory, "old.pdf");
            var newest = Path.Combine(directory, "new.pdf");
            File.WriteAllText(working, "working");
            File.WriteAllText(oldIssue, "old immutable issue");
            File.WriteAllText(newest, "new immutable issue");
            var run = new TestRunRecord
            {
                Reports =
                [
                    new() { Kind = ReportKinds.Certification, Role = ReportArtifactRoles.Working, PdfPath = working },
                    new() { Kind = ReportKinds.Certification, Role = ReportArtifactRoles.Issued, PdfPath = newest,
                        GeneratedAt = DateTimeOffset.UnixEpoch.AddDays(2) },
                    new() { Kind = ReportKinds.Certification, Role = ReportArtifactRoles.Issued, PdfPath = oldIssue,
                        GeneratedAt = DateTimeOffset.UnixEpoch.AddDays(1) },
                ],
            };

            var exports = ResultsViewModel.CollectExportReportFiles(run).ToArray();

            Assert.Equal(newest, ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
            Assert.Equal(oldIssue, ReportAttestationService.ResolvePrintOrExportPdfPath(run, oldIssue));
            Assert.Equal(newest, ReportAttestationService.ResolvePrintOrExportPdfPath(run, working));
            Assert.Contains((newest, "certification.pdf"), exports);
            Assert.Contains((working, Path.Combine("working", "certification.pdf")), exports);
            Assert.Contains((oldIssue, Path.Combine("history", "certification", "old", "certification.pdf")), exports);
            Assert.Equal(3, run.Reports.Count);
            Assert.True(ReportAttestationService.RunOwnsPdf(run, oldIssue));
            Assert.Equal("old immutable issue", File.ReadAllText(oldIssue));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
