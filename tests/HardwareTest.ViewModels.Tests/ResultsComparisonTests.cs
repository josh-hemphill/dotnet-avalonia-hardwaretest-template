using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Storage;
using HardwareTest.Features.Results;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ResultsComparisonTests
{
    [Fact]
    public async Task Open_loads_compare_with_previous_rows()
    {
        var store = new FakeRunStore();
        var t0 = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            RunId = "prior-run",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-C",
            StartedAt = t0,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 10.0, Timestamp = t0, Unit = "V" }],
        });
        store.Seed(new TestRunRecord
        {
            RunId = "current-run",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-C",
            StartedAt = t0.AddHours(1),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 9.0, Timestamp = t0.AddHours(1), Unit = "V" }],
            Steps =
            [
                new StepResultRecord
                {
                    StepId = "s1",
                    StepType = "Acquire",
                    Passed = true,
                    Message = "ok",
                    StartedAt = t0.AddHours(1),
                    CompletedAt = t0.AddHours(1),
                },
            ],
        });

        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            comparison: new RunComparisonService(store));
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "current-run");
        await vm.OpenCommand.ExecuteAsync();

        Assert.True(vm.HasComparison);
        Assert.Contains("prior-run", vm.ComparisonSummary, StringComparison.Ordinal);
        var row = Assert.Single(vm.ComparisonMetrics);
        Assert.Equal("VDC", row.MetricKey);
        Assert.Contains("9", row.CurrentText, StringComparison.Ordinal);
        Assert.Contains("10", row.PreviousText, StringComparison.Ordinal);
        Assert.Contains("%", row.DeltaText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, row.Note);
    }

    [Fact]
    public async Task Open_shows_unavailable_note_when_metric_missing_on_previous()
    {
        var store = new FakeRunStore();
        var t0 = new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-U",
            StartedAt = t0,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 1.0, Timestamp = t0 }],
        });
        store.Seed(new TestRunRecord
        {
            RunId = "cur",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-U",
            StartedAt = t0.AddMinutes(30),
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample { Channel = "VDC", Value = 1.0, Timestamp = t0.AddMinutes(30) },
                new StoredSample { Channel = "IDC", Value = 0.1, Timestamp = t0.AddMinutes(30) },
            ],
        });

        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            comparison: new RunComparisonService(store));
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs.First(r => r.RunId == "cur");
        await vm.OpenCommand.ExecuteAsync();

        var idc = Assert.Single(vm.ComparisonMetrics, r => r.MetricKey == "IDC");
        Assert.Equal("Not in previous run", idc.Note);
        Assert.Equal("—", idc.PreviousText);
    }

    [Fact]
    public async Task Open_without_previous_still_shows_comparison_summary()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "solo",
            PlanId = "sample",
            PlanName = "Sample",
            DutSerial = "SN-S",
            StartedAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Value = 1.0, Timestamp = DateTimeOffset.UnixEpoch }],
        });

        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            comparison: new RunComparisonService(store));
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();

        Assert.True(vm.HasComparison);
        Assert.Empty(vm.ComparisonMetrics);
        Assert.Contains("No previous run", vm.ComparisonSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_sets_schema_drift_strip_when_a_run_is_read_only()
    {
        var store = new FakeRunStore();
        store.Seed(new TestRunRecord
        {
            RunId = "future",
            PlanName = "Sample",
            StartedAt = new DateTimeOffset(2026, 8, 24, 13, 0, 0, TimeSpan.Zero),
            Result = RunResult.Passed,
            IsSchemaReadOnly = true,
            StoredSchemaVersion = 99,
        });

        var vm = new ResultsViewModel(store, new FakeReportService());
        Assert.False(vm.HasSchemaDrift);

        await vm.RefreshCommand.ExecuteAsync();

        Assert.True(vm.HasSchemaDrift);
        Assert.Contains("read-only", vm.SchemaDriftSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_package_includes_diagnostics_txt()
    {
        var store = new FakeRunStore();
        var t0 = new DateTimeOffset(2026, 8, 24, 14, 0, 0, TimeSpan.Zero);
        store.Seed(new TestRunRecord
        {
            RunId = "exp-1",
            PlanId = "sample",
            PlanName = "Sample",
            StartedAt = t0,
            Result = RunResult.Passed,
            AppVersion = "0.1.0-test",
        });
        var export = new CapturingExportTargetService();
        var buildInfo = BuildInfo.FromAssembly(typeof(ResultsViewModel).Assembly);
        var vm = new ResultsViewModel(
            store,
            new FakeReportService(),
            exportTargets: export,
            buildInfo: buildInfo);
        await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedRun = vm.Runs[0];
        await vm.OpenCommand.ExecuteAsync();
        await vm.ExportPackageCommand.ExecuteAsync();

        Assert.NotNull(export.LastPackageDir);
        var diagnostics = Path.Combine(export.LastPackageDir!, "diagnostics.txt");
        Assert.True(File.Exists(diagnostics));
        var text = await File.ReadAllTextAsync(diagnostics);
        Assert.Contains("HardwareTest diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("RunId: exp-1", text, StringComparison.Ordinal);
        Assert.Contains("Catalog self-check", text, StringComparison.Ordinal);
        Assert.Contains("Exported package", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class CapturingExportTargetService : IExportTargetService
{
    public string? LastPackageDir { get; private set; }

    public IReadOnlyList<ExportTarget> ListTargets()
        =>
        [
            new ExportTarget
            {
                Id = "test",
                DisplayName = "Test export",
                RootPath = Path.GetTempPath(),
            },
        ];

    public string WriteAtomic(ExportTarget target, string relativePath, byte[] content, long? minFreeBytes = null)
        => throw new NotSupportedException();

    public string ExportPackage(
        ExportTarget target,
        string packageFolderName,
        IEnumerable<(string SourcePath, string RelativeName)> files)
    {
        var dest = Path.Combine(
            Path.GetTempPath(),
            "ht-cmp-export-" + Guid.NewGuid().ToString("N"),
            packageFolderName);
        Directory.CreateDirectory(dest);
        foreach (var (source, relative) in files)
        {
            var outPath = Path.Combine(dest, relative);
            var parent = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, outPath, overwrite: true);
        }

        LastPackageDir = dest;
        return dest;
    }
}
