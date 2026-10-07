using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class TypstReportServiceTests
{
    [Fact]
    public async Task GeneratePdfAsync_writes_pdf_and_updates_run()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        var path = await CompileOrSkipAsync(() => reports.GeneratePdfAsync(run));

        Assert.True(File.Exists(path));
        AssertPdfMagic(await File.ReadAllBytesAsync(path));

        var reloaded = await runStore.LoadAsync(run.RunId);
        Assert.Equal(path, ReportAttestationService.ResolveWorkingPdfPath(reloaded!, ReportKinds.Status));
    }

    [Fact]
    public async Task CompileTemplateAsync_charts_from_samples_without_png_paths()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun(sampleCount: 8);
        await runStore.SaveAsync(run);
        Assert.Empty(run.PlotImagePaths);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = true });
        var pdf = await CompileOrSkipAsync(() => reports.CompileTemplateAsync(run));
        AssertPdfMagic(pdf);

        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, ReportKinds.Status);
        Assert.True(File.Exists(Path.Combine(workDir, "main.typ")));
        Assert.True(File.Exists(Path.Combine(workDir, "run.json")));
        Assert.True(File.Exists(Path.Combine(workDir, "lib", "sample-chart.typ")));

        var json = await File.ReadAllTextAsync(Path.Combine(workDir, "run.json"));
        Assert.Contains("\"samples\"", json, StringComparison.Ordinal);
        Assert.Contains("\"channel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Samples\"", json, StringComparison.Ordinal);

        var chartLib = await File.ReadAllTextAsync(Path.Combine(workDir, "lib", "sample-chart.typ"));
        Assert.Contains("samples", chartLib, StringComparison.Ordinal);
        Assert.Contains("channel", chartLib, StringComparison.Ordinal);
    }

    [Fact]
    public void SamplePlotExporter_still_writes_png_when_called_directly()
    {
        using var temp = new TempDataDirectory();
        var run = CreateRun(sampleCount: 4);
        var plotsDir = Path.Combine(temp.Path, "plots");
        var paths = SamplePlotExporter.ExportAllChannels(run, plotsDir);
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.True(File.Exists(p) && new FileInfo(p).Length > 0));
    }

    [Theory]
    [InlineData("test-report.typ")]
    [InlineData("custom-report.typ")]
    [InlineData("status-report.typ")]
    public async Task CompileTemplateAsync_uses_explicit_DataDirectory_template(string templateName)
    {
        using var temp = new TempDataDirectory();
        var reportsDir = Path.Combine(temp.Path, "reports");
        Directory.CreateDirectory(reportsDir);
        File.WriteAllText(
            Path.Combine(reportsDir, templateName),
            """
            #set page(width: 100mm, height: 50mm)
            = Override template
            Run: #sys.inputs.runId
            """);

        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
                ReportTemplateName = templateName,
            });
        var pdf = await CompileOrSkipAsync(() => reports.CompileTemplateAsync(run));
        AssertPdfMagic(pdf);

        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, ReportKinds.Status);
        var main = await File.ReadAllTextAsync(Path.Combine(workDir, "main.typ"));
        Assert.Contains("Override template", main, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateReportsAsync_writes_status_and_certification_pdfs()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        var artifacts = await CompileOrSkipAsync(() => reports.GenerateReportsAsync(
            run,
            [ReportKinds.Status, ReportKinds.Certification],
            new DutHistoryReport { OperatorSummary = "DUT history OK vs last 1 run(s).", OverallSeverity = DutHistorySeverity.Normal }));

        Assert.Equal(2, artifacts.Count);
        Assert.All(artifacts, a => Assert.True(File.Exists(a.PdfPath)));
        Assert.Contains(artifacts, a => a.Kind == ReportKinds.Status);
        Assert.Contains(artifacts, a => a.Kind == ReportKinds.Certification);
        Assert.Equal(artifacts.First(a => a.Kind == ReportKinds.Status).PdfPath, ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Status));
        Assert.All(artifacts, a => Assert.Equal(ReportArtifactRoles.Working, a.Role));

        var reloaded = await runStore.LoadAsync(run.RunId);
        Assert.Equal(2, reloaded!.Reports.Count);
        Assert.All(reloaded.Reports, a => Assert.True(ReportArtifactRoles.IsWorking(a.Role)));
    }

    [Fact]
    public async Task GenerateReportsAsync_preserves_prior_certification_attestation()
    {
        using var temp = new TempDataDirectory();
        await WriteCertificationTypstAsync(
            temp.Path,
            """
            #set page(width: 120mm, height: 60mm)
            #if sys.inputs.attestationDetail != "" [
              #panic("prior certifier reprinted")
            ]
            = Unstamped regenerate
            """);
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);
        var sidecar = Path.Combine(runStore.GetRunDirectory(run.RunId), "certification.attestation.json");
        await File.WriteAllTextAsync(sidecar, "{}");
        run.Attestations.Add(new ReportAttestation
        {
            Kind = AttestationKind.Presence,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Previous Certifier",
            Serial = "OLD-CARD",
            SidecarPath = sidecar,
        });

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
            });
        var artifacts = await CompileOrSkipAsync(() => reports.GenerateReportsAsync(run, [ReportKinds.Certification]));

        Assert.Single(run.Attestations);
        Assert.True(File.Exists(sidecar));
        AssertPdfMagic(await File.ReadAllBytesAsync(artifacts[0].PdfPath));
        var workDir = Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, ReportKinds.Certification);
        var json = await File.ReadAllTextAsync(Path.Combine(workDir, "run.json"));
        Assert.DoesNotContain("Previous Certifier", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateReportsAsync_keeps_status_when_restamping_certification()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        await CompileOrSkipAsync(() => reports.GenerateReportsAsync(
            run,
            [ReportKinds.Status, ReportKinds.Certification]));
        Assert.Equal(2, run.Reports.Count);
        var statusPath = run.Reports.Single(r => r.Kind == ReportKinds.Status).PdfPath;

        await CompileOrSkipAsync(() => reports.GenerateReportsAsync(run, [ReportKinds.Certification]));

        Assert.Equal(2, run.Reports.Count);
        Assert.Contains(run.Reports, r => r.Kind == ReportKinds.Status && r.PdfPath == statusPath);
        Assert.Contains(run.Reports, r => r.Kind == ReportKinds.Certification);
        Assert.Equal(statusPath, ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Status));
    }

    [Fact]
    public async Task GenerateReportsAsync_keeps_issued_pdf_and_attestation()
    {
        using var temp = new TempDataDirectory();
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(runStore, new AppSettings { EmbedPlotsInReport = false });
        await CompileOrSkipAsync(() => reports.GenerateReportsAsync(run, [ReportKinds.Certification]));

        var dir = runStore.GetRunDirectory(run.RunId);
        var workingPath = ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification)!;
        var workingBytes = await File.ReadAllBytesAsync(workingPath);
        var issuedDir = Path.Combine(dir, ReportArtifactRoles.DirectoryName);
        Directory.CreateDirectory(issuedDir);
        var issuedPath = Path.Combine(issuedDir, "certification.pdf");
        await File.WriteAllBytesAsync(issuedPath, workingBytes);
        run.Reports.Add(new RunReportArtifact
        {
            Kind = ReportKinds.Certification,
            Title = ReportKinds.Title(ReportKinds.Certification),
            PdfPath = issuedPath,
            GeneratedAt = DateTimeOffset.UtcNow,
            Role = ReportArtifactRoles.Issued,
        });
        var sidecar = Path.Combine(dir, "certification.attestation.json");
        await File.WriteAllTextAsync(sidecar, "{}");
        run.Attestations.Add(new ReportAttestation
        {
            Kind = AttestationKind.Presence,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Issued Certifier",
            Serial = "ISSUE-1",
            SidecarPath = sidecar,
        });
        await runStore.SaveAsync(run);

        await CompileOrSkipAsync(() => reports.GenerateReportsAsync(run, [ReportKinds.Certification]));

        Assert.Single(run.Attestations);
        Assert.Equal("Issued Certifier", run.Attestations[0].DisplayName);
        Assert.True(File.Exists(sidecar));
        Assert.True(File.Exists(issuedPath));
        Assert.Equal(workingBytes, await File.ReadAllBytesAsync(issuedPath));
        Assert.True(File.Exists(workingPath));
        Assert.Contains(
            run.Reports,
            r => r.Kind == ReportKinds.Certification && ReportArtifactRoles.IsIssued(r.Role) && r.PdfPath == issuedPath);
        Assert.Contains(
            run.Reports,
            r => r.Kind == ReportKinds.Certification && ReportArtifactRoles.IsWorking(r.Role));
        Assert.Equal(issuedPath, ReportAttestationService.ResolvePdfPath(run, ReportKinds.Certification));
        Assert.Equal(workingPath, ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task GenerateReportsAsync_compileIdentity_does_not_persist_attestation()
    {
        using var temp = new TempDataDirectory();
        await WriteCertificationTypstAsync(
            temp.Path,
            """
            #set page(width: 120mm, height: 60mm)
            #if not sys.inputs.attestationDetail.contains("Jane Certifier") [
              #panic("missing party")
            ]
            #if not sys.inputs.attestationDetail.contains("CARD-1") [
              #panic("missing serial")
            ]
            #if sys.inputs.attestationKind != "contact" [
              #panic("missing transport")
            ]
            = Certified by
            #sys.inputs.attestationDetail
            #sys.inputs.attestationKind
            """);

        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);
        var overlay = new ReportAttestation
        {
            Kind = string.Empty,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Jane Certifier",
            Serial = "CARD-1",
            Transport = CredentialTransport.Contact,
            CapturedAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
        };

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
            });
        var artifacts = await CompileOrSkipAsync(() => reports.GenerateReportsAsync(
            run,
            [ReportKinds.Certification],
            compileIdentity: overlay));

        Assert.Single(artifacts);
        Assert.Empty(run.Attestations);
        Assert.False(File.Exists(Path.Combine(runStore.GetRunDirectory(run.RunId), "certification.attestation.json")));
        AssertPdfMagic(await File.ReadAllBytesAsync(artifacts[0].PdfPath));
    }

    [Fact]
    public async Task CompileReportAsync_does_not_invalidate_or_write()
    {
        using var temp = new TempDataDirectory();
        await WriteCertificationTypstAsync(
            temp.Path,
            """
            #set page(width: 120mm, height: 60mm)
            #if not sys.inputs.attestationDetail.contains("Jane Certifier") [
              #panic("missing overlay party")
            ]
            #if sys.inputs.attestationDetail.contains("Prior") [
              #panic("prior certifier reprinted")
            ]
            = Overlay only
            """);
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        var dir = runStore.GetRunDirectory(run.RunId);
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "certification.pdf");
        var prior = "%PDF-1.4 prior"u8.ToArray();
        await File.WriteAllBytesAsync(pdfPath, prior);
        var sidecar = Path.Combine(dir, "certification.attestation.json");
        await File.WriteAllTextAsync(sidecar, "{}");
        run.Attestations.Add(new ReportAttestation
        {
            Kind = AttestationKind.Signed,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Prior",
            SidecarPath = sidecar,
        });
        await runStore.SaveAsync(run);

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
            });
        var overlay = new ReportAttestation
        {
            Kind = AttestationKind.Signed,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Jane Certifier",
            Serial = "CARD-1",
            Transport = CredentialTransport.Contact,
        };
        var bytes = await CompileOrSkipAsync(() => reports.CompileReportAsync(run, ReportKinds.Certification, compileIdentity: overlay));

        AssertPdfMagic(bytes);
        Assert.Equal(prior, await File.ReadAllBytesAsync(pdfPath));
        Assert.True(File.Exists(sidecar));
        Assert.Single(run.Attestations);
        Assert.Equal("Prior", run.Attestations[0].DisplayName);
        var json = await File.ReadAllTextAsync(
            Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, ReportKinds.Certification, "run.json"));
        Assert.DoesNotContain("Prior", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Jane Certifier", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompileReportAsync_without_overlay_omits_prior_attestation()
    {
        using var temp = new TempDataDirectory();
        await WriteCertificationTypstAsync(
            temp.Path,
            """
            #set page(width: 120mm, height: 60mm)
            #if sys.inputs.attestationDetail != "" [
              #panic("prior certifier reprinted")
            ]
            = No overlay
            """);
        var runStore = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await runStore.SaveAsync(run);
        run.Attestations.Add(new ReportAttestation
        {
            Kind = AttestationKind.Presence,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Previous Certifier",
            Serial = "OLD-CARD",
        });

        using var reports = new TypstReportService(
            runStore,
            new AppSettings
            {
                DataDirectory = temp.Path,
                EmbedPlotsInReport = false,
            });
        var bytes = await CompileOrSkipAsync(() => reports.CompileReportAsync(run, ReportKinds.Certification));

        AssertPdfMagic(bytes);
        Assert.Single(run.Attestations);
        var json = await File.ReadAllTextAsync(
            Path.Combine(Path.GetTempPath(), "HardwareTestTypst", run.RunId, ReportKinds.Certification, "run.json"));
        Assert.DoesNotContain("Previous Certifier", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-report.typ")]
    [InlineData("status-report.typ")]
    public async Task CompileTemplateAsync_missing_requested_template_fails_without_alias(string templateName)
    {
        using var temp = new TempDataDirectory();
        using var reports = new TypstReportService(new FileRunStore(temp.RunsDirectory),
            new AppSettings { ReportTemplateName = templateName });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reports.CompileTemplateAsync(CreateRun()));

        Assert.Contains(templateName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public async Task GenerateReportsAsync_rejected_run_preserves_pdf_and_attestation(int version, bool readOnly)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        await store.SaveAsync(run);
        var dir = store.GetRunDirectory(run.RunId);
        var pdfPath = Path.Combine(dir, "status.pdf");
        var sidecarPath = Path.Combine(dir, "status.attestation.json");
        var priorPdf = "%PDF-1.4 frozen"u8.ToArray();
        await File.WriteAllBytesAsync(pdfPath, priorPdf);
        await File.WriteAllTextAsync(sidecarPath, "frozen attestation");
        run.Attestations.Add(new ReportAttestation { ReportKind = ReportKinds.Status, SidecarPath = sidecarPath });
        var priorJson = await File.ReadAllBytesAsync(Path.Combine(dir, "run.json"));
        run.SchemaVersion = version;
        run.IsSchemaReadOnly = readOnly;
        using var reports = new TypstReportService(store, new AppSettings());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reports.GenerateReportsAsync(run, [ReportKinds.Status]));

        Assert.Equal(priorPdf, await File.ReadAllBytesAsync(pdfPath));
        Assert.Equal("frozen attestation", await File.ReadAllTextAsync(sidecarPath));
        Assert.Equal(priorJson, await File.ReadAllBytesAsync(Path.Combine(dir, "run.json")));
        Assert.Single(run.Attestations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task GenerateReportsAsync_current_candidate_cannot_mutate_blocked_destination(int storedVersion)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        var dir = store.GetRunDirectory(run.RunId);
        Directory.CreateDirectory(dir);
        var json = "{\"schemaVersion\":" + storedVersion + "}";
        await File.WriteAllTextAsync(Path.Combine(dir, "run.json"), json);
        var pdfPath = Path.Combine(dir, "status.pdf");
        await File.WriteAllTextAsync(pdfPath, "frozen PDF");
        using var reports = new TypstReportService(store, new AppSettings());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => reports.GeneratePdfAsync(run));

        Assert.Equal(json, await File.ReadAllTextAsync(Path.Combine(dir, "run.json")));
        Assert.Equal("frozen PDF", await File.ReadAllTextAsync(pdfPath));
        Assert.Empty(run.Reports);
    }


    [Theory]
    [InlineData("{\"schemaVersion\":3}", "unsupported")]
    [InlineData("{\"schemaVersion\":999}", "future")]
    [InlineData("{invalid", "corrupt")]
    [InlineData("{\"schemaVersion\":4,\"samples\":{}}", "corrupt")]
    public async Task Sole_blocked_backup_prevents_report_and_attestation_mutation(string backup, string failure)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        var dir = store.GetRunDirectory(run.RunId);
        var path = Path.Combine(dir, "run.json");
        await File.WriteAllTextAsync(path + ".bak", backup);
        var before = await File.ReadAllBytesAsync(path + ".bak");
        var pdf = Path.Combine(dir, "status.pdf");
        var sidecar = Path.Combine(dir, "status.attestation.json");
        await File.WriteAllTextAsync(pdf, "frozen PDF");
        await File.WriteAllTextAsync(sidecar, "frozen stamp");
        run.Attestations.Add(new ReportAttestation { ReportKind = ReportKinds.Status, SidecarPath = sidecar });
        using var reports = new TypstReportService(store, new AppSettings());
        if (failure == "future")
            await Assert.ThrowsAsync<SchemaReadOnlyException>(() => reports.GeneratePdfAsync(run));
        else if (failure == "unsupported")
            await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => reports.GeneratePdfAsync(run));
        else
            await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => reports.GeneratePdfAsync(run));
        Assert.False(File.Exists(path));
        Assert.Equal(before, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.Equal("frozen PDF", await File.ReadAllTextAsync(pdf));
        Assert.Equal("frozen stamp", await File.ReadAllTextAsync(sidecar));
        Assert.Single(run.Attestations);
        Assert.Empty(run.Reports);
        if (failure == "future")
            Assert.Throws<SchemaReadOnlyException>(() => ReportAttestationService.InvalidateForKinds(run, dir, [ReportKinds.Status]));
        else if (failure == "unsupported")
            Assert.Throws<UnsupportedDocumentSchemaException>(() => ReportAttestationService.InvalidateForKinds(run, dir, [ReportKinds.Status]));
        else
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => ReportAttestationService.InvalidateForKinds(run, dir, [ReportKinds.Status]));
        Assert.Single(run.Attestations);
        Assert.Equal("frozen stamp", await File.ReadAllTextAsync(sidecar));
    }

    [Fact]
    public async Task GenerateReportsAsync_preserves_nonworking_roles_for_generated_kind()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = CreateRun();
        var review = new RunReportArtifact
        {
            Kind = ReportKinds.Status,
            Role = "review",
            PdfPath = Path.Combine(temp.Path, "review.pdf"),
        };
        await File.WriteAllTextAsync(review.PdfPath, "review artifact");
        run.Reports.Add(review);
        await store.SaveAsync(run);
        using var reports = new TypstReportService(store, new AppSettings { EmbedPlotsInReport = false });

        await CompileOrSkipAsync(() => reports.GeneratePdfAsync(run));

        var preserved = Assert.Single(run.Reports, r => r.PdfPath == review.PdfPath && r.Role == review.Role);
        Assert.Equal(JsonSerializer.Serialize(review), JsonSerializer.Serialize(preserved));
        Assert.Single(run.Reports, r => ReportArtifactRoles.IsWorking(r.Role));
        Assert.Equal("review artifact", await File.ReadAllTextAsync(review.PdfPath));
    }

    [Fact]
    public async Task GenerateSuitePdfAsync_readonly_future_child_reports_child_schema_and_preserves_pdf()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var suite = new SuiteRunRecord
        {
            SchemaVersion = SchemaVersions.SuiteRunRecord,
            SuiteRunId = "blocked-suite",
            IsSchemaReadOnly = true,
            PlanRuns =
            [
                new TestRunRecord
                {
                    SchemaVersion = SchemaVersions.TestRunRecord + 1,
                    StoredSchemaVersion = SchemaVersions.TestRunRecord + 1,
                    RunId = "future-child",
                    IsSchemaReadOnly = true,
                    AppVersion = "future-writer",
                },
            ],
        };
        var dir = store.GetRunDirectory(suite.SuiteRunId);
        Directory.CreateDirectory(dir);
        var pdf = Path.Combine(dir, "status.pdf");
        await File.WriteAllTextAsync(pdf, "frozen suite PDF");
        using var reports = new TypstReportService(store, new AppSettings());

        var error = await Assert.ThrowsAsync<SchemaReadOnlyException>(() => reports.GenerateSuitePdfAsync(suite));

        Assert.Equal(SchemaDocumentTypes.TestRunRecord, error.Status.DocumentType);
        Assert.Equal(SchemaVersions.TestRunRecord + 1, error.Status.StoredVersion);
        Assert.Contains("future-writer", error.Message, StringComparison.Ordinal);
        Assert.Equal("frozen suite PDF", await File.ReadAllTextAsync(pdf));
        Assert.Null(suite.ReportPdfPath);
        Assert.False(File.Exists(Path.Combine(dir, "run.json")));
    }

    [Fact]
    public void Artifact_paths_require_matching_kind_and_role_and_ownership()
    {
        var run = CreateRun();
        run.Reports =
        [
            new RunReportArtifact { Kind = ReportKinds.Status, Role = ReportArtifactRoles.Working, PdfPath = "status.pdf" },
            new RunReportArtifact { Kind = ReportKinds.Status, Role = ReportArtifactRoles.Issued, PdfPath = "issued/status.pdf" },
        ];

        Assert.Null(ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification));
        Assert.Null(ReportAttestationService.ResolveWorkingPdfPath(run, "custom"));
        Assert.True(ReportAttestationService.RunOwnsPdf(run, "status.pdf"));
        Assert.True(ReportAttestationService.RunOwnsPdf(run, "issued/status.pdf"));
        Assert.False(ReportAttestationService.RunOwnsPdf(run, "unlisted.pdf"));
        Assert.Throws<InvalidOperationException>(() => ReportAttestationService.KindForPdf(run, "unlisted.pdf"));
        Assert.Equal("issued/status.pdf", ReportAttestationService.ResolvePrintOrExportPdfPath(run, "status.pdf"));
    }

    private static Task WriteCertificationTypstAsync(string dataDirectory, string body)
    {
        var reportsDir = Path.Combine(dataDirectory, "reports");
        Directory.CreateDirectory(reportsDir);
        return File.WriteAllTextAsync(Path.Combine(reportsDir, "certification-report.typ"), body);
    }

    private static TestRunRecord CreateRun(int sampleCount = 1)
    {
        var samples = Enumerable.Range(0, sampleCount)
            .Select(i => new StoredSample
            {
                Channel = "VDC",
                StepPath = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
                Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i),
                Value = 1.0 + (i * 0.1),
            })
            .ToList();

        return new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = Guid.NewGuid().ToString("N"),
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "DUT-1",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = samples,
        };
    }

    private static async Task<T> CompileOrSkipAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Typst native library was not restored for the selected runtime identifier.",
                ex);
        }
    }

    private static void AssertPdfMagic(byte[] bytes)
    {
        Assert.True(bytes.Length > 100);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }
}
