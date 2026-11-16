using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsExportDestinationTests
{
    [Theory]
    [InlineData("run.json")]
    [InlineData("working PDF")]
    [InlineData("issued PDF")]
    [InlineData("historical issued PDF")]
    [InlineData("issued snapshot")]
    [InlineData("attestation sidecar")]
    public async Task Missing_declared_evidence_blocks_all_destination_writes_until_exact_evidence_is_restored(string missing)
    {
        using var fixture = new ExportFixture();
        string missingPath;
        if (missing is "run.json" or "working PDF")
        {
            missingPath = missing == "run.json" ? fixture.RunJsonPath : fixture.Record.Reports[0].PdfPath;
        }
        else
        {
            // Real mock-presence issuance creates immutable PDF/snapshot/sidecar evidence.
            Assert.True((await fixture.IssueAsync()).Succeeded);
            if (missing == "historical issued PDF") Assert.True((await fixture.IssueAsync()).Succeeded);
            var issued = fixture.Record.Reports.Where(report => ReportArtifactRoles.IsIssued(report.Role))
                .OrderBy(report => report.RevisionNumber).First();
            missingPath = missing switch
            {
                "issued snapshot" => issued.RunSnapshotPath!,
                "attestation sidecar" => fixture.Record.Attestations.First(stamp => stamp.RevisionId == issued.RevisionId).SidecarPath!,
                _ => issued.PdfPath,
            };
        }
        fixture.PersistRecord();
        var sourceJson = File.ReadAllBytes(fixture.RunJsonPath);
        var sourceReports = fixture.Record.Reports.Select(report => (report.PdfPath, Bytes: File.ReadAllBytes(report.PdfPath))).ToArray();
        var original = File.ReadAllBytes(missingPath);
        var vm = await fixture.OpenAsync();
        vm.SelectedExportTarget = vm.ExportTargets[1];
        File.Delete(missingPath);

        await vm.ExportPackageCommand.ExecuteAsync();

        Assert.Contains("Export blocked", vm.Status, StringComparison.Ordinal);
        Assert.Contains("missing", vm.Status, StringComparison.Ordinal);
        Assert.Contains(missing == "working PDF" ? "Regenerate working reports" : "Restore", vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("Exported package", vm.Status, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.FirstRoot));
        Assert.False(Directory.Exists(fixture.SecondRoot));
        Assert.Equal(fixture.Record.RunId, vm.OpenedRun?.RunId);
        Assert.Equal(fixture.SecondRoot, vm.SelectedExportTarget?.RootPath);

        await File.WriteAllBytesAsync(missingPath, original);
        await vm.ExportPackageCommand.ExecuteAsync();

        fixture.AssertPackage(fixture.SecondRoot);
        Assert.False(Directory.Exists(fixture.FirstRoot));
        Assert.Equal(sourceJson, File.ReadAllBytes(fixture.RunJsonPath));
        foreach (var source in sourceReports) Assert.Equal(source.Bytes, File.ReadAllBytes(source.PdfPath));
        var defaultReport = ReportRevisions.Latest(fixture.Record, ReportKinds.Status)?.PdfPath
            ?? fixture.Record.Reports[0].PdfPath;
        Assert.Equal(File.ReadAllBytes(defaultReport), File.ReadAllBytes(Path.Combine(fixture.SecondRoot, "run-export-source", "status.pdf")));
    }

    [Fact]
    public async Task Selected_nonfirst_destination_receives_the_opened_run_package_after_target_refresh()
    {
        using var fixture = new ExportFixture();
        var vm = await fixture.OpenAsync();
        vm.SelectedExportTarget = vm.ExportTargets[1];

        await vm.ExportPackageCommand.ExecuteAsync();

        fixture.AssertPackage(fixture.SecondRoot);
        Assert.False(Directory.Exists(Path.Combine(fixture.FirstRoot, "run-export-source")));
        Assert.Equal(fixture.SecondRoot, vm.SelectedExportTarget?.RootPath);
        Assert.Contains(fixture.SecondRoot, vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initial_default_destination_is_usable_without_an_extra_selection()
    {
        using var fixture = new ExportFixture();
        var vm = await fixture.OpenAsync();
        Assert.Equal(fixture.FirstRoot, vm.SelectedExportTarget?.RootPath);

        await vm.ExportPackageCommand.ExecuteAsync();

        fixture.AssertPackage(fixture.FirstRoot);
        Assert.False(Directory.Exists(fixture.SecondRoot));
    }

    [Fact]
    public async Task Removed_selected_destination_never_falls_back_until_the_operator_chooses_a_replacement()
    {
        using var fixture = new ExportFixture();
        var vm = await fixture.OpenAsync();
        vm.SelectedExportTarget = vm.ExportTargets[1];
        fixture.SecondPresent = false;

        await vm.ExportPackageCommand.ExecuteAsync();

        Assert.Null(vm.SelectedExportTarget);
        Assert.Contains("no longer available", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose a destination", vm.Status, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.FirstRoot));
        Assert.False(Directory.Exists(fixture.SecondRoot));
        await vm.ExportPackageCommand.ExecuteAsync();
        Assert.Contains("Choose an export destination", vm.Status, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.FirstRoot));

        vm.SelectedExportTarget = Assert.Single(vm.ExportTargets);
        await vm.ExportPackageCommand.ExecuteAsync();
        fixture.AssertPackage(fixture.FirstRoot);
    }

    [Fact]
    public async Task Configured_destination_id_does_not_authorize_a_changed_root_path()
    {
        using var fixture = new ExportFixture();
        fixture.Settings.PreferRemovableExport = false;
        fixture.Settings.ExportDirectory = fixture.SecondRoot;
        var vm = await fixture.OpenAsync();
        Assert.Equal("configured", vm.SelectedExportTarget?.Id);
        fixture.Settings.ExportDirectory = fixture.ReplacementRoot;

        await vm.ExportPackageCommand.ExecuteAsync();

        Assert.Null(vm.SelectedExportTarget);
        Assert.Contains("no longer available", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.SecondRoot));
        Assert.False(Directory.Exists(fixture.ReplacementRoot));
        Assert.False(Directory.Exists(fixture.FirstRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deferred_certification_keeps_the_requested_destination_and_rechecks_its_presence(bool removed)
    {
        using var fixture = new ExportFixture(certified: true);
        var vm = await fixture.OpenAsync();
        vm.SelectedExportTarget = vm.ExportTargets[1];
        await vm.ExportPackageCommand.ExecuteAsync();
        Assert.True(vm.ShowAttestationPrompt);
        Assert.False(Directory.Exists(fixture.FirstRoot));
        Assert.False(Directory.Exists(fixture.SecondRoot));
        // A later UI selection cannot redirect an already pending certified action.
        vm.SelectedExportTarget = vm.ExportTargets[0];
        if (removed) fixture.SecondPresent = false;

        await vm.CaptureAttestationCommand.ExecuteAsync();

        Assert.False(vm.ShowAttestationPrompt);
        Assert.True(vm.HasAttestation);
        Assert.False(Directory.Exists(fixture.FirstRoot));
        if (removed)
        {
            Assert.Null(vm.SelectedExportTarget);
            Assert.Contains("no longer available", vm.Status, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(fixture.SecondRoot));
        }
        else
        {
            fixture.AssertPackage(fixture.SecondRoot);
            Assert.True(File.Exists(Path.Combine(fixture.SecondRoot, "run-export-source", "certification.attestation.json")));
            Assert.Equal(fixture.SecondRoot, vm.SelectedExportTarget?.RootPath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_real_settings_refresh_available_destinations_preserve_selection_and_require_explicit_replacement(bool saveSettings)
    {
        var root = Path.Combine(Path.GetTempPath(), "export-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsStore(root); await settings.LoadAsync();
            using var fixture = new ExportFixture(persistentSettings: settings);
            var vm = await fixture.OpenAsync();
            vm.SelectedExportTarget = vm.ExportTargets.Single(target => target.RootPath == fixture.SecondRoot);
            settings.AppSettings.ExportDirectory = fixture.ReplacementRoot;
            await Refresh();
            Assert.Contains(vm.ExportTargets, target => target.RootPath == fixture.ReplacementRoot);
            Assert.Equal(fixture.SecondRoot, vm.SelectedExportTarget?.RootPath);

            vm.SelectedExportTarget = vm.ExportTargets.Single(target => target.Id == "configured");
            var replacement = Path.Combine(fixture.ReplacementRoot, "new-destination");
            settings.AppSettings.ExportDirectory = replacement;
            await Refresh();
            Assert.Null(vm.SelectedExportTarget);
            Assert.Contains("no longer available", vm.Status, StringComparison.Ordinal);
            await vm.ExportPackageCommand.ExecuteAsync();
            Assert.Contains("Choose an export destination", vm.Status, StringComparison.Ordinal);
            Assert.False(Directory.Exists(fixture.FirstRoot));
            Assert.False(Directory.Exists(fixture.SecondRoot));
            Assert.False(Directory.Exists(fixture.ReplacementRoot));
            vm.SelectedExportTarget = vm.ExportTargets.Single(target => target.RootPath == replacement);
            await vm.ExportPackageCommand.ExecuteAsync();
            fixture.AssertPackage(replacement);
            Assert.False(Directory.Exists(fixture.FirstRoot));
            Assert.False(Directory.Exists(fixture.SecondRoot));

            Task Refresh() => saveSettings ? settings.SaveAppSettingsAsync() : vm.RefreshCommand.ExecuteAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_save_during_pending_certification_never_redirects_the_captured_destination(bool removed)
    {
        var root = Path.Combine(Path.GetTempPath(), "pending-export-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsStore(root); await settings.LoadAsync();
            using var fixture = new ExportFixture(certified: true, persistentSettings: settings);
            var vm = await fixture.OpenAsync();
            vm.SelectedExportTarget = vm.ExportTargets.Single(target => target.RootPath == fixture.SecondRoot);
            await vm.ExportPackageCommand.ExecuteAsync();
            Assert.True(vm.ShowAttestationPrompt);
            vm.SelectedExportTarget = vm.ExportTargets.Single(target => target.RootPath == fixture.FirstRoot);
            settings.AppSettings.ExportDirectory = fixture.ReplacementRoot;
            fixture.SecondPresent = !removed;
            await settings.SaveAppSettingsAsync();
            Assert.True(vm.ShowAttestationPrompt);
            Assert.Equal(fixture.FirstRoot, vm.SelectedExportTarget?.RootPath);
            Assert.Contains(vm.ExportTargets, target => target.RootPath == fixture.ReplacementRoot);
            await vm.CaptureAttestationCommand.ExecuteAsync();
            Assert.False(vm.ShowAttestationPrompt);
            Assert.False(Directory.Exists(fixture.FirstRoot));
            Assert.False(Directory.Exists(fixture.ReplacementRoot));
            if (removed)
            {
                Assert.Null(vm.SelectedExportTarget);
                Assert.Contains("no longer available", vm.Status, StringComparison.Ordinal);
                Assert.False(Directory.Exists(fixture.SecondRoot));
            }
            else fixture.AssertPackage(fixture.SecondRoot);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class ExportFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "operator-export-" + Guid.NewGuid().ToString("N"));
        private readonly FakeRunStore _store = new();
        private readonly bool _certified;
        private readonly TestRunRecord _run;
        private string _runJson;
        internal TestRunRecord Record => _run;
        internal string RunJsonPath => Path.Combine(_store.GetRunDirectory(_run.RunId), "run.json");
        private readonly SettingsStore? _persistentSettings;
        internal AppSettings Settings { get; }
        internal bool SecondPresent { get; set; } = true;
        internal string FirstRoot => Path.Combine(_root, "media-a");
        internal string SecondRoot => Path.Combine(_root, "media-b");
        internal string ReplacementRoot => Path.Combine(_root, "replacement");

        internal ExportFixture(bool certified = false, SettingsStore? persistentSettings = null)
        {
            _persistentSettings = persistentSettings;
            Settings = persistentSettings?.AppSettings ?? new AppSettings();
            Settings.PreferRemovableExport = true;
            _certified = certified;
            Settings.RequireAttestationBeforeExport = certified;
            Settings.AllowPresenceInLieuOfSigning = certified;
            var runDirectory = _store.GetRunDirectory("export-source");
            Directory.CreateDirectory(runDirectory);
            var report = Path.Combine(runDirectory, certified ? "certification.pdf" : "status.pdf");
            File.WriteAllBytes(report, "%PDF-1.4 operator export evidence"u8.ToArray());
            _run = new TestRunRecord
            {
                SchemaVersion = SchemaVersions.TestRunRecord,
                RunId = "export-source",
                PlanId = "persona-plan",
                PlanName = "Persona acceptance",
                DutSerial = "DUT-EVIDENCE-42",
                OperatorName = "Source operator",
                Result = RunResult.Failed,
                StartedAt = DateTimeOffset.UtcNow,
                Reports =
                [
                    new RunReportArtifact
                    {
                        Kind = certified ? ReportKinds.Certification : ReportKinds.Status,
                        Title = "Operator evidence",
                        PdfPath = report,
                        GeneratedAt = DateTimeOffset.UtcNow,
                        Role = ReportArtifactRoles.Working,
                    },
                ],
            };
            _runJson = JsonSerializer.Serialize(_run, AppJsonContext.Default.TestRunRecord);
            File.WriteAllText(Path.Combine(runDirectory, "run.json"), _runJson);
            _store.Seed(_run);
        }

        internal Task<ReportAttestationResult> IssueAsync()
        {
            Settings.AllowPresenceInLieuOfSigning = true;
            return new ReportAttestationService(new MockOperatorCredentialBroker(canSign: false), _store, Settings)
                .AttestAsync(_run, _run.Reports[0].Kind);
        }

        internal void PersistRecord()
        {
            _runJson = JsonSerializer.Serialize(_run, AppJsonContext.Default.TestRunRecord);
            File.WriteAllText(RunJsonPath, _runJson);
        }

        internal async Task<ResultsViewModel> OpenAsync()
        {
            var export = new ExportTargetService(Settings, Path.Combine(_root, "data"), removableRoots: () =>
            {
                // Discovery returns new instances every time, as the production detector does.
                var targets = new List<ExportTarget>
                {
                    new() { Id = "removable:a", DisplayName = "Media A", RootPath = FirstRoot, IsRemovable = true },
                };
                if (SecondPresent)
                    targets.Add(new() { Id = "removable:b", DisplayName = "Media B", RootPath = SecondRoot, IsRemovable = true });
                return targets;
            });
            var vm = new ResultsViewModel(_store, new FakeReportService(), exportTargets: export,
                attestation: _certified ? new ReportAttestationService(
                    new MockOperatorCredentialBroker(canSign: false), _store, Settings) : null,
                settings: Settings, settingsStore: _persistentSettings)
            { UiScheduler = action => action() };
            await vm.RefreshCommand.ExecuteAsync();
            vm.SelectedRun = Assert.Single(vm.Runs);
            await vm.OpenCommand.ExecuteAsync();
            Assert.Equal(_run.RunId, vm.OpenedRun?.RunId);
            return vm;
        }

        internal void AssertPackage(string targetRoot)
        {
            var package = Path.Combine(targetRoot, "run-export-source");
            Assert.Equal(_runJson, File.ReadAllText(Path.Combine(package, "run.json")));
            Assert.Contains("RunId: export-source", File.ReadAllText(Path.Combine(package, "diagnostics.txt")), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(package, _certified ? "certification.pdf" : "status.pdf")));
        }

        public void Dispose()
        {
            _store.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
