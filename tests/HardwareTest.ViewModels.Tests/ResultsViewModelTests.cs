using System.Reactive;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using HardwareTest.ViewModels.Tests.Time;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsViewModelTests
{
    [Fact]
    public async Task Open_shows_steps_and_dut_columns()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "r2",
            PlanName = "Sample",
            DutSerial = "SN-R",
            DutPartNumber = "PN-R",
            SessionId = "sess123456",
            OperatorName = "Op",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "Identity",
                    StepName = "Identity",
                    AttemptCount = 1,
                    PassedCount = 1,
                    LatestPassed = true,
                    LatestMessage = "ok",
                    Attempts =
                    [
                        new StepResultRecord
                        {
                            StepPath = "Identity",
                            AttemptNumber = 1,
                            StepId = "Identity",
                            StepType = "IdentityCheckStep",
                            Passed = true,
                            Message = "ok",
                            StartedAt = DateTimeOffset.UtcNow,
                            CompletedAt = DateTimeOffset.UtcNow,
                        },
                    ],
                },
            ],
            Samples =
            [
                new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.1 },
            ],
        });
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal("SN-R", vm.Runs[0].DutSerial);
        Assert.Equal("PN-R", vm.Runs[0].DutPartNumber);
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.ShowDetail);
        Assert.NotEmpty(vm.StepDetails);
        Assert.NotEmpty(vm.SampleDetails);
    }

    [Fact]
    public async Task Open_caps_step_and_sample_sidebar_rows()
    {
        var store = new FakeRunStore();
        var stepAttempts = Enumerable.Range(0, 250)
            .Select(i => new StepAttemptSummary
            {
                StepPath = $"step-{i}",
                StepName = $"step-{i}",
                AttemptCount = 1,
                PassedCount = 1,
                LatestPassed = true,
                LatestMessage = "ok",
                Attempts =
                [
                    new StepResultRecord
                    {
                        StepId = $"step-{i}",
                        StepPath = $"step-{i}",
                        StepType = "Acquire",
                        AttemptNumber = 1,
                        Passed = true,
                        Message = "ok",
                        StartedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                    },
                ],
            })
            .ToList();
        var samples = Enumerable.Range(0, 250)
            .Select(i => new StoredSample
            {
                Channel = "VDC",
                Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(i),
                Value = i,
            })
            .ToList();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "big",
            PlanName = "Sample",
            DutSerial = "SN-BIG",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            StepAttempts = stepAttempts,
            Samples = samples,
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        Assert.Equal(201, vm.StepDetails.Count);
        Assert.Contains(vm.StepDetails, line => line.Contains("more steps", StringComparison.Ordinal));
        Assert.Equal(201, vm.SampleDetails.Count);
        Assert.Contains(vm.SampleDetails, line => line.Contains("more samples", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Open_shows_dut_history_when_priors_exist()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-H",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "current",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-H",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 8.5, Timestamp = DateTimeOffset.UtcNow, HistoryEnabled = true }],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var history = new DutHistoryService(store);
        var vm = new ResultsViewModel(store, new FakeReportService(), history);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "current");
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasHistory);
        Assert.Contains("Alert", vm.HistorySummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(DutHistorySeverity.Alert), vm.HistorySeverity);
    }

    [Fact]
    public async Task Open_sample_details_include_metric_key_and_role()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "pres-1",
            PlanName = "Sample",
            DutSerial = "SN-P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.25,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.Contains(vm.SampleDetails, line =>
            line.Contains("VDC", StringComparison.OrdinalIgnoreCase)
            && line.Contains("timeseries", StringComparison.OrdinalIgnoreCase)
            && line.Contains("V", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Open_builds_presentation_chart_and_gauge_tiles()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "pres-tiles",
            PlanName = "Sample",
            DutSerial = "SN-T",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.0,
                    Timestamp = DateTimeOffset.UtcNow,
                },
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Value = 1.2,
                    Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(5),
                },
                new StoredSample
                {
                    Channel = "Mean",
                    MetricKey = "VDC.mean",
                    DisplayRole = "scalar",
                    Unit = "V",
                    Value = 1.1,
                    LimitLow = 0,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasPresentationTiles);
        Assert.Contains(vm.PresentationTiles, t => t.IsChart && t.YsLength == 2);
        Assert.Contains(vm.PresentationTiles, t => t.IsGauge && t.MetricKey == "VDC.mean");
    }

    [Fact]
    public async Task Refresh_loads_runs()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "r1",
            PlanName = "P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        });
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Runs);
        Assert.Contains("1 run", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Open_without_selection_sets_status()
    {
        var vm = new ResultsViewModel(new FakeRunStore(), new FakeReportService());
        await vm.OpenCommand.ExecuteAsync();
        Assert.Contains("Select a run", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reprint_raises_report_opened()
    {
        var store = new FakeRunStore();
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "r1",
            PlanName = "P",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        store.Seed(run);
        var reports = new FakeReportService { PdfPath = Path.Combine(Path.GetTempPath(), "rpt.pdf") };
        await File.WriteAllTextAsync(reports.PdfPath, "pdf");
        var vm = new ResultsViewModel(store, reports);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];

        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        await vm.ReprintCommand.ExecuteAsync();

        Assert.Equal(reports.PdfPath, opened);
        Assert.Equal(1, reports.GenerateCount);
    }

    [Fact]
    public async Task Open_certification_does_not_require_attestation()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-view"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-view",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            attestation: new ReportAttestationService(
                new MockOperatorCredentialBroker(canSign: false),
                store,
                settings),
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        vm.OpenReportCommand.Execute(vm.ReportItems[0]).Subscribe();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(pdf, opened);
        Assert.Empty(run.Attestations);

        opened = null;
        await vm.OpenDefaultReportCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(pdf, opened);
        Assert.Empty(run.Attestations);
    }

    [Fact]
    public async Task Open_run_with_attestation_shows_certifier_summary()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-shown"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var issuedPdf = Path.Combine(store.GetRunDirectory("cert-shown"), ReportArtifactRoles.DirectoryName,
            $"certification-{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(issuedPdf)!);
        var issuedBytes = "%PDF-1.4 issued certification"u8.ToArray();
        await File.WriteAllBytesAsync(issuedPdf, issuedBytes);
        var attestation = new ReportAttestation
        {
            Kind = AttestationKind.Signed,
            ReportKind = ReportKinds.Certification,
            DisplayName = "Jane Certifier",
            Serial = "CARD-1",
            Transport = CredentialTransport.Contact,
            CapturedAt = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero),
            SidecarPath = Path.ChangeExtension(issuedPdf, ".attestation.json"),
            PdfSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(issuedBytes)),
        };
        await File.WriteAllTextAsync(attestation.SidecarPath!, System.Text.Json.JsonSerializer.Serialize(
            new ReportAttestationSidecar { Attestation = attestation }, AppJsonContext.Default.ReportAttestationSidecar));
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-shown",
            PlanName = "Sample",
            OperatorName = "Session Technician",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Issued,
                    Kind = ReportKinds.Certification,
                    Title = "Issued Certification Report",
                    PdfPath = issuedPdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
            Attestations = [attestation],
        });
        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasAttestation);
        Assert.Equal("signed: Jane Certifier (contact, CARD-1) at 2026-09-18 12:00:00Z", vm.AttestationSummary);
    }

    [Fact]
    public async Task Capture_attestation_restamps_via_report_service()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-stamp"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-stamp",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var reports = new FakeReportService { PdfPath = pdf };
        var vm = new ResultsViewModel(
            store,
            reports,
            attestation: new ReportAttestationService(
                new MockOperatorCredentialBroker(canSign: false),
                store,
                settings,
                reports: new Lazy<IReportService>(() => reports)),
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        vm.OpenReportCommand.Execute(vm.ReportItems[0]).Subscribe();
        await vm.ExportPackageCommand.ExecuteAsync();
        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(1, reports.GenerateCount);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, reports.LastCompileIdentity?.DisplayName);
        Assert.True(vm.HasAttestation);
        Assert.Contains(MockOperatorCredentialBroker.MockDisplayName, vm.AttestationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_and_open_certification_show_attestation_overlay_until_badge()
    {
        using var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-1"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        await File.WriteAllTextAsync(Path.Combine(store.GetRunDirectory("cert-1"), "stray.attestation.json"), "unrelated evidence");
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var attestation = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false),
            store,
            settings);
        var export = new CapturingExportTargetService();
        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            exportTargets: export,
            attestation: attestation,
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        vm.OpenReportCommand.Execute(vm.ReportItems[0]).Subscribe();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(pdf, opened);

        await vm.ExportPackageCommand.ExecuteAsync();
        Assert.True(vm.ShowAttestationPrompt);
        Assert.Null(export.LastPackageDir);

        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(pdf, opened);
        Assert.Equal(AttestationKind.Presence, run.Attestations[0].Kind);
        Assert.True(vm.HasAttestation);
        Assert.Contains(MockOperatorCredentialBroker.MockDisplayName, vm.AttestationSummary, StringComparison.Ordinal);

        await vm.ExportPackageCommand.ExecuteAsync();
        Assert.Contains("Exported package", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(export.LastPackageDir);
        Assert.True(File.Exists(Path.Combine(export.LastPackageDir!, "certification.attestation.json")));
        Assert.False(File.Exists(Path.Combine(export.LastPackageDir!, "stray.attestation.json")));
        Assert.Equal(
            await File.ReadAllBytesAsync(run.Attestations[0].SidecarPath!),
            await File.ReadAllBytesAsync(Path.Combine(export.LastPackageDir!, "certification.attestation.json")));
    }

    [Fact]
    public async Task Export_certification_signs_with_mock_without_pin()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-sign"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-sign",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = false,
        };
        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            attestation: new ReportAttestationService(
                new MockOperatorCredentialBroker(canSign: true),
                store,
                settings),
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        await vm.ExportPackageCommand.ExecuteAsync();
        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.False(vm.ShowAttestationPin);
        Assert.Equal(AttestationKind.Signed, run.Attestations[0].Kind);
    }

    [Fact]
    public async Task Certification_overlay_prompts_pin_then_signs()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-pin"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-pin",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = false,
        };
        var original = await File.ReadAllBytesAsync(pdf);
        var reports = new FakeReportService { PdfPath = pdf };
        var vm = new ResultsViewModel(
            store,
            reports,
            attestation: new ReportAttestationService(
                new PinRequiredMockBroker(),
                store,
                settings,
                reports: new Lazy<IReportService>(() => reports)),
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        await vm.ExportPackageCommand.ExecuteAsync();
        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.True(vm.ShowAttestationPrompt);
        Assert.True(vm.ShowAttestationPin);
        Assert.Empty(run.Attestations);
        Assert.False(vm.HasAttestation);
        Assert.Equal(original, await File.ReadAllBytesAsync(pdf));
        Assert.Equal(0, reports.GenerateCount);

        vm.AttestationPin = PinRequiredMockBroker.Pin;
        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(AttestationKind.Signed, run.Attestations[0].Kind);
        Assert.Equal(string.Empty, vm.AttestationPin);
        Assert.True(vm.HasAttestation);
        Assert.Equal(original, await File.ReadAllBytesAsync(pdf));
        var issued = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issued));
        Assert.NotEqual(original, await File.ReadAllBytesAsync(issued!));
    }

    [Fact]
    public async Task Print_certification_shows_attestation_overlay_until_badge()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(store.GetRunDirectory("cert-print"), "certification.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4"u8.ToArray());
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "cert-print",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        store.Seed(run);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            attestation: new ReportAttestationService(
                new MockOperatorCredentialBroker(canSign: false),
                store,
                settings),
            settings: settings);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        string? printReady = null;
        vm.CertifiedPrintReady += (_, path) => printReady = path;
        await vm.RequestCertifiedPrintAsync(pdf);
        Assert.True(vm.ShowAttestationPrompt);
        Assert.Null(printReady);

        await vm.CaptureAttestationCommand.ExecuteAsync();
        Assert.False(vm.ShowAttestationPrompt);
        var issued = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issued));
        Assert.Equal(issued, printReady);
        Assert.Equal(AttestationKind.Presence, run.Attestations[0].Kind);

        printReady = null;
        await vm.RequestCertifiedPrintAsync(pdf);
        Assert.False(vm.ShowAttestationPrompt);
        Assert.Equal(issued, printReady);
    }

    [Fact]
    public async Task Selecting_run_opens_detail_without_opening_report()
    {
        var store = new FakeRunStore();
        var pdf = Path.Combine(Path.GetTempPath(), "status-sel.pdf");
        await File.WriteAllTextAsync(pdf, "pdf");
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "sel-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Status,
                    Title = "Status",
                    PdfPath = pdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "s1",
                    StepName = "s1",
                    AttemptCount = 1,
                    PassedCount = 1,
                    LatestPassed = true,
                    LatestMessage = "ok",
                    Attempts =
                    [
                        new StepResultRecord
                        {
                            StepPath = "s1",
                            AttemptNumber = 1,
                            StepId = "s1",
                            StepType = "Acquire",
                            Passed = true,
                            Message = "ok",
                            StartedAt = DateTimeOffset.UtcNow,
                            CompletedAt = DateTimeOffset.UtcNow,
                        },
                    ],
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        vm.SelectedRun = vm.Runs[0];
        await Task.Delay(50);
        Assert.True(vm.ShowDetail);
        Assert.NotEmpty(vm.StepDetails);
        Assert.Null(opened);
    }

    [Fact]
    public async Task Open_default_report_uses_catalog_default_kind()
    {
        var store = new FakeRunStore();
        var statusPdf = Path.Combine(Path.GetTempPath(), "def-status.pdf");
        var certPdf = Path.Combine(Path.GetTempPath(), "def-cert.pdf");
        await File.WriteAllTextAsync(statusPdf, "pdf");
        await File.WriteAllTextAsync(certPdf, "pdf");
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "def-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Certification,
                    Title = "Certification",
                    PdfPath = certPdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Status,
                    Title = "Status",
                    PdfPath = statusPdf,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await Task.Delay(50);
        string? opened = null;
        vm.ReportOpened += (_, path) => opened = path;
        await vm.OpenSelectedRunDefaultReportAsync();
        Assert.Equal(statusPdf, opened);
        Assert.Contains(vm.ReportItems, r => r.IsDefault && r.Kind == ReportKinds.Status);
    }

    [Fact]
    public void ResolveDefaultReportPath_prefers_default_kind()
    {
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            PlanId = "sample",
            Reports =
            [
                new RunReportArtifact { Role = ReportArtifactRoles.Working, Kind = ReportKinds.Certification, PdfPath = "c.pdf" },
                new RunReportArtifact { Role = ReportArtifactRoles.Working, Kind = ReportKinds.Status, PdfPath = "s.pdf" },
            ],
        };
        Assert.Equal("s.pdf", ResultsViewModel.ResolveDefaultReportPath(run));
    }

    [Fact]
    public void ResolveDefaultReportPath_prefers_working_when_issued_exists()
    {
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            PlanId = "sample",
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Status,
                    PdfPath = "issued/status.pdf",
                    Role = ReportArtifactRoles.Issued,
                },
                new RunReportArtifact
                {
                    Kind = ReportKinds.Status,
                    PdfPath = "status.pdf",
                    Role = ReportArtifactRoles.Working,
                },
            ],
        };
        Assert.Equal("status.pdf", ResultsViewModel.ResolveDefaultReportPath(run));
    }

    [Fact]
    public void ResolveDefaultReportPath_issued_only_has_no_working_default()
    {
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            PlanId = "sample",
            Reports = [new RunReportArtifact { Kind = ReportKinds.Status, Role = ReportArtifactRoles.Issued, PdfPath = "issued/status.pdf" }],
        };

        Assert.Null(ResultsViewModel.ResolveDefaultReportPath(run));
        Assert.Equal("issued/status.pdf", ReportAttestationService.ResolvePdfPath(run, ReportKinds.Status));
    }

    [Fact]
    public void ResolveDefaultWorkingPdfPath_supports_custom_kind_and_skips_nonworking_artifacts()
    {
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            Reports =
            [
                new RunReportArtifact { Kind = "custom", Role = ReportArtifactRoles.Issued, PdfPath = "issued/custom.pdf" },
                new RunReportArtifact { Kind = ReportKinds.Status, Role = ReportArtifactRoles.Working, PdfPath = "status.pdf" },
                new RunReportArtifact { Kind = "custom", Role = ReportArtifactRoles.Working, PdfPath = "custom.pdf" },
            ],
        };

        Assert.Equal("custom.pdf", ReportAttestationService.ResolveDefaultWorkingPdfPath(run, "custom"));
        Assert.Equal("status.pdf", ReportAttestationService.ResolveDefaultWorkingPdfPath(run, "missing"));
    }

    [Fact]
    public void CollectExportReportFiles_uses_issued_as_canonical_pdf()
    {
        var root = Path.Combine(Path.GetTempPath(), "ht-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "issued"));
        var working = Path.Combine(root, "certification.pdf");
        var issued = Path.Combine(root, "issued", "certification.pdf");
        File.WriteAllText(working, "working");
        File.WriteAllText(issued, "issued");
        try
        {
            var run = new TestRunRecord
            {
                SchemaVersion = SchemaVersions.TestRunRecord,
                Reports =
                [
                    new RunReportArtifact
                    {
                        Kind = ReportKinds.Certification,
                        PdfPath = working,
                        Role = ReportArtifactRoles.Working,
                    },
                    new RunReportArtifact
                    {
                        Kind = ReportKinds.Certification,
                        PdfPath = issued,
                        Role = ReportArtifactRoles.Issued,
                    },
                ],
            };

            var files = ResultsViewModel.CollectExportReportFiles(run).ToList();
            Assert.Contains(files, f => f.SourcePath == issued && f.RelativeName == "certification.pdf");
            Assert.Contains(files, f => f.SourcePath == working && f.RelativeName == Path.Combine("working", "certification.pdf"));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Could not delete temp export files '{root}': {ex.Message}");
            }
        }
    }

    [Fact]
    public void CollectExportReportFiles_keeps_revision_history_without_collisions_or_latest_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "export-revisions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string Seed(string file) { var path = Path.Combine(root, file); File.WriteAllText(path, file); return path; }
            var older = Seed("old.pdf");
            var working = Seed("working.pdf");
            var sidecar = Seed("old.attestation.json");
            var snapshot = Seed("snapshot.json");
            var run = new TestRunRecord
            {
                Reports =
                [
                    new() { Kind = "certification", Role = ReportArtifactRoles.Issued, RevisionId = "rev1", RevisionNumber = 1, PdfPath = older, RunSnapshotPath = snapshot },
                    new() { Kind = "certification", Role = ReportArtifactRoles.Issued, RevisionId = "rev2", RevisionNumber = 2, PdfPath = Path.Combine(root, "missing.pdf") },
                    new() { Kind = "certification", PdfPath = working },
                ],
                Attestations = [new() { ReportKind = "certification", RevisionId = "rev1", SidecarPath = sidecar,
                    PdfSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(older))) }],
            };
            var files = ResultsViewModel.CollectExportReportFiles(run).ToList();
            Assert.Equal(files.Count, files.Select(f => f.RelativeName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(files, f => f.RelativeName == "certification.pdf");
            Assert.Contains(files, f => f.RelativeName == Path.Combine("working", "certification.pdf"));
            Assert.Contains(files, f => f.RelativeName == Path.Combine("history", "certification", "rev1", "certification.pdf"));
            Assert.Contains(files, f => f.RelativeName == Path.Combine("history", "certification", "rev1", "certification.attestation.json"));
            Assert.Contains(files, f => f.RelativeName == Path.Combine("history", "certification", "rev1", "run.snapshot.json"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Package_export_never_replaces_unavailable_issued_pdf_with_working_path(bool nullPath)
    {
        var store = new FakeRunStore();
        var working = Path.Combine(store.GetRunDirectory("legacy-export"), "status.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(working)!);
        await File.WriteAllTextAsync(working, "working bytes");
        store.Seed(new TestRunRecord
        {
            RunId = "legacy-export",
            PlanId = "sample",
            Reports = [new()
            {
                Kind = ReportKinds.Status, Role = ReportArtifactRoles.Issued,
                RevisionId = "latest", RevisionNumber = 1,
                PdfPath = nullPath ? null! : Path.Combine(Path.GetDirectoryName(working)!, "missing.pdf"),
            }],
        });
        var export = new CapturingExportTargetService();
        var vm = new ResultsViewModel(store, new FakeReportService(), exportTargets: export);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        await vm.ExportPackageCommand.ExecuteAsync();
        Assert.NotNull(export.LastPackageDir);
        Assert.False(File.Exists(Path.Combine(export.LastPackageDir!, "status.pdf")));
    }

    [Fact]
    public async Task Report_list_shows_working_and_issued()
    {
        var store = new FakeRunStore();
        var dir = store.GetRunDirectory("both-1");
        Directory.CreateDirectory(Path.Combine(dir, ReportArtifactRoles.DirectoryName));
        var working = Path.Combine(dir, "certification.pdf");
        var issued = Path.Combine(dir, ReportArtifactRoles.DirectoryName, "certification.pdf");
        await File.WriteAllTextAsync(working, "working");
        await File.WriteAllTextAsync(issued, "issued");
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "both-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Reports =
            [
                new RunReportArtifact
                {
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = working,
                    Role = ReportArtifactRoles.Working,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
                new RunReportArtifact
                {
                    Kind = ReportKinds.Certification,
                    Title = "Certification Report",
                    PdfPath = issued,
                    Role = ReportArtifactRoles.Issued,
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        Assert.Equal(2, vm.ReportItems.Count);
        Assert.Contains(vm.ReportItems, r => r.RoleLabel == "Working" && r.PdfPath == working && !r.IsIssued);
        Assert.Contains(vm.ReportItems, r => r.RoleLabel == "Issued" && r.PdfPath == issued && r.IsIssued);
        Assert.DoesNotContain(vm.ReportItems, r => r.IsDefault);
    }

    [Fact]
    public void GuessRunIdFromPdfPath_walks_up_from_issued_folder()
    {
        var issued = Path.Combine("runs", "run-abc", ReportArtifactRoles.DirectoryName, "certification.pdf");
        Assert.Equal("run-abc", ReportAttestationService.GuessRunIdFromPdfPath(issued));
        Assert.Equal("run-abc", ReportAttestationService.GuessRunIdFromPdfPath(Path.Combine("runs", "run-abc", "certification.pdf")));
    }

    [Fact]
    public async Task Search_and_result_filter_narrow_run_list()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "a",
            PlanId = "sample",
            PlanName = "Sample Hardware Suite",
            DutSerial = "SN-A",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Result = RunResult.Passed,
        });
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "b",
            PlanId = "board-demo",
            PlanName = "Board Demo",
            DutSerial = "SN-B",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Result = RunResult.Failed,
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(2, vm.Runs.Count);

        vm.SearchText = "Board";
        Assert.Single(vm.Runs);
        Assert.Equal("b", vm.Runs[0].RunId);

        vm.SearchText = string.Empty;
        vm.ResultFilter = nameof(RunResult.Passed);
        Assert.Single(vm.Runs);
        Assert.Equal("a", vm.Runs[0].RunId);
        Assert.Contains("Showing 1 of 2", vm.FilterStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_populates_history_metric_rows_and_reports()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-M",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 10, Timestamp = DateTimeOffset.UtcNow }],
        });
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "current",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-M",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = 8, Timestamp = DateTimeOffset.UtcNow, HistoryEnabled = true }],
            Reports =
            [
                new RunReportArtifact
                {
                    Role = ReportArtifactRoles.Working,
                    Kind = ReportKinds.Status,
                    Title = "Status Report",
                    PdfPath = Path.Combine(Path.GetTempPath(), "status.pdf"),
                    GeneratedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        var history = new DutHistoryService(store);
        var vm = new ResultsViewModel(store, new FakeReportService(), history);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "current");
        await vm.OpenCommand.ExecuteAsync();
        Assert.True(vm.HasHistory);
        Assert.NotEmpty(vm.HistoryMetrics);
        Assert.True(vm.HasReports);
        Assert.Contains(vm.ReportItems, r => r.Kind == ReportKinds.Status);
    }

    [Fact]
    public async Task Operator_and_date_filters_narrow_run_list_with_yield()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 24, 18, 0, 0, TimeSpan.Zero));
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "today-pass",
            PlanName = "Sample",
            OperatorName = "Ada",
            StartedAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            Result = RunResult.Passed,
        });
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "today-fail",
            PlanName = "Sample",
            OperatorName = "Ada",
            StartedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            Result = RunResult.Failed,
        });
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "old-fail",
            PlanName = "Sample",
            OperatorName = "Bob",
            StartedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            Result = RunResult.Failed,
        });

        var vm = new ResultsViewModel(store, new FakeReportService(), clock: clock);
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(3, vm.Runs.Count);
        Assert.Contains("Passed 1", vm.FilterStatus, StringComparison.Ordinal);
        Assert.Contains("Failed 2", vm.FilterStatus, StringComparison.Ordinal);

        vm.OperatorFilter = "Ada";
        Assert.Equal(2, vm.Runs.Count);
        Assert.Contains("Passed 1", vm.YieldSummary, StringComparison.Ordinal);
        Assert.Contains("Failed 1", vm.YieldSummary, StringComparison.Ordinal);

        vm.DateFilter = ResultsViewModel.DateToday;
        Assert.Equal(2, vm.Runs.Count);
        Assert.DoesNotContain(vm.Runs, r => r.RunId == "old-fail");

        vm.OperatorFilter = ResultsViewModel.AllFilter;
        vm.DateFilter = ResultsViewModel.DateLast7Days;
        Assert.Equal(2, vm.Runs.Count);
        Assert.Contains(vm.Runs, r => r.RunId == "today-pass");
        Assert.Contains(vm.Runs, r => r.RunId == "today-fail");
    }

    [Fact]
    public async Task OpenRunById_after_fail_sets_result_filter_and_failed_steps_only()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "pass",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            Result = RunResult.Passed,
            Steps = [new StepResultRecord { StepId = "Ok", StepType = "Id", Passed = true, Message = "ok" }],
        });
        var t1 = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 4, 1, 0, 0, 2, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "fail",
            PlanName = "Sample",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Result = RunResult.Failed,
            Steps =
            [
                new StepResultRecord { StepId = "Ok", StepPath = "Ok", StepType = "Id", Passed = true, Message = "ok", CompletedAt = t1 },
                new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = false, Message = "out", CompletedAt = t2 },
            ],
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "Ok",
                    StepName = "Ok",
                    AttemptCount = 1,
                    PassedCount = 1,
                    LatestPassed = true,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Ok", StepPath = "Ok", StepType = "Id", Passed = true, Message = "ok", CompletedAt = t1 },
                    ],
                },
                new StepAttemptSummary
                {
                    StepPath = "Bad",
                    StepName = "Bad",
                    AttemptCount = 2,
                    PassedCount = 1,
                    FailedCount = 1,
                    LatestPassed = false,
                    Attempts =
                    [
                        new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = true, AttemptNumber = 1, Message = "ok", CompletedAt = t1 },
                        new StepResultRecord { StepId = "Bad", StepPath = "Bad", StepType = "Acquire", Passed = false, AttemptNumber = 2, Message = "out", CompletedAt = t2 },
                    ],
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.OpenRunByIdAsync("fail");

        Assert.Equal(nameof(RunResult.Failed), vm.ResultFilter);
        Assert.Single(vm.Runs);
        Assert.Equal("fail", vm.SelectedRun?.RunId);
        Assert.True(vm.ShowFailedStepsOnly);
        Assert.True(vm.HasFirstFail);
        Assert.Contains("Bad", vm.FirstFailSummary, StringComparison.Ordinal);
        Assert.Contains(vm.StepDetails, line => line.Contains("First fail", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("2 (1F/1P)", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("#2 FAIL", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.StepDetails, line => line.Contains("[Id]", StringComparison.Ordinal) && line.Contains("PASS"));

        vm.ShowFailedStepsOnly = false;
        Assert.Contains(vm.StepDetails, line => line.Contains("[Id]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task First_fail_marker_and_attempts_follow_path_across_retries()
    {
        var store = new FakeRunStore();
        var t1 = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 4, 2, 0, 0, 2, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "retry-fail",
            PlanName = "Sample",
            StartedAt = t2,
            Result = RunResult.Failed,
            StepAttempts =
            [
                new StepAttemptSummary
                {
                    StepPath = "Bad",
                    StepName = "Bad",
                    AttemptCount = 2,
                    FailedCount = 2,
                    LatestPassed = false,
                    Attempts =
                    [
                        new StepResultRecord
                        {
                            StepId = "Bad",
                            StepPath = "Bad",
                            StepType = "Acquire",
                            Passed = false,
                            AttemptNumber = 1,
                            Message = "first",
                            CompletedAt = t1,
                        },
                        new StepResultRecord
                        {
                            StepId = "Bad",
                            StepPath = "Bad",
                            StepType = "Acquire",
                            Passed = false,
                            AttemptNumber = 2,
                            Message = "retry",
                            CompletedAt = t2,
                        },
                    ],
                },
            ],
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        await vm.OpenRunByIdAsync("retry-fail");

        Assert.True(vm.HasFirstFail);
        Assert.Contains(vm.StepDetails, line => line.Contains("First fail", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("#1 FAIL", StringComparison.Ordinal));
        Assert.Contains(vm.StepDetails, line => line.Contains("#2 FAIL", StringComparison.Ordinal));
    }
}

internal sealed class PinRequiredMockBroker : IOperatorCredentialBroker
{
    public const string Pin = "123456";

    private readonly MockOperatorCredentialBroker _inner = new(canSign: true);

    public bool IsMock => true;
    public bool CanSign => true;
    public string? SigningAlgorithm => MockOperatorCredentialBroker.MockAlgorithm;
    public string StatusText => _inner.StatusText;

    public Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => _inner.WaitForPresenceAsync(timeout, cancellationToken);

    public Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return Task.FromResult(CredentialSignResult.NeedPin("Enter badge PIN to sign."));
        }

        if (pin != Pin)
        {
            return Task.FromResult(CredentialSignResult.Failed("Incorrect PIN (2 retries left).", 2));
        }

        return _inner.TrySignPayloadAsync(payload, credential, pin, cancellationToken);
    }
}
