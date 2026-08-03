using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Serialization;

public sealed class SchemaVersioningTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "schema", name);

    [Fact]
    public async Task Legacy_run_fixture_loads_as_legacy()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dest = Path.Combine(store.GetRunDirectory("legacy-run-1"), "run.json");
        File.Copy(FixturePath("run-v0-legacy.json"), dest, overwrite: true);

        var run = await store.LoadAsync("legacy-run-1");
        Assert.NotNull(run);
        Assert.True(run!.IsLegacy);
        Assert.False(run.IsSchemaReadOnly);
        Assert.Equal(0, run.StoredSchemaVersion);
        Assert.Null(run.Samples[0].HistoryEnabled);
    }

    [Fact]
    public async Task Future_run_fixture_loads_read_only_and_save_is_rejected()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dest = Path.Combine(store.GetRunDirectory("future-run-1"), "run.json");
        File.Copy(FixturePath("run-v999-future.json"), dest, overwrite: true);

        var run = await store.LoadAsync("future-run-1");
        Assert.NotNull(run);
        Assert.True(run!.IsSchemaReadOnly);
        Assert.Equal(999, run.StoredSchemaVersion);
        Assert.Equal("9.9.9+ffff.20990101000000", run.AppVersion);

        var before = await File.ReadAllTextAsync(dest);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(run));
        var after = await File.ReadAllTextAsync(dest);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Round_trip_stamps_current_schema_version()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord
        {
            RunId = "stamp-1",
            PlanName = "Plan",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };

        await store.SaveAsync(run);
        var loaded = await store.LoadAsync("stamp-1");
        Assert.NotNull(loaded);
        Assert.Equal(SchemaVersions.TestRunRecord, loaded!.SchemaVersion);
        Assert.False(loaded.IsLegacy);
        Assert.False(loaded.IsSchemaReadOnly);

        var json = await File.ReadAllTextAsync(Path.Combine(store.GetRunDirectory("stamp-1"), "run.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(SchemaVersions.TestRunRecord, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task V1_run_fixture_loads_as_current()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dest = Path.Combine(store.GetRunDirectory("current-run-1"), "run.json");
        File.Copy(FixturePath("run-v1.json"), dest, overwrite: true);

        var run = await store.LoadAsync("current-run-1");
        Assert.NotNull(run);
        Assert.False(run!.IsLegacy);
        Assert.False(run.IsSchemaReadOnly);
        Assert.Equal(1, run.StoredSchemaVersion);
        Assert.True(run.Samples[0].HistoryEnabled);
    }

    [Fact]
    public async Task Legacy_settings_fixture_loads_and_future_refuses_save()
    {
        using var temp = new TempDataDirectory();
        File.Copy(
            FixturePath("settings-v0-legacy.json"),
            Path.Combine(temp.Path, "settings.json"),
            overwrite: true);

        var legacyStore = new SettingsStore(temp.Path);
        await legacyStore.LoadAsync();
        Assert.Null(legacyStore.SettingsSchemaWarning);
        Assert.True(legacyStore.IsSettingsWritable);

        using var futureTemp = new TempDataDirectory();
        File.Copy(
            FixturePath("settings-v999-future.json"),
            Path.Combine(futureTemp.Path, "settings.json"),
            overwrite: true);
        var warnings = new List<string>();
        var futureStore = new SettingsStore(futureTemp.Path);
        await futureStore.LoadAsync(null, null, warnings.Add);
        Assert.False(futureStore.IsSettingsWritable);
        Assert.False(string.IsNullOrWhiteSpace(futureStore.SettingsSchemaWarning));
        Assert.Contains(warnings, w => w.Contains("Read-only", StringComparison.OrdinalIgnoreCase));

        futureStore.AppSettings.ThemePreference = "Dark";
        await futureStore.SaveAppSettingsAsync();
        var disk = await File.ReadAllTextAsync(futureStore.SettingsPath);
        Assert.Contains("\"schemaVersion\": 999", disk, StringComparison.Ordinal);
        Assert.DoesNotContain("\"themePreference\": \"Dark\"", disk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upgrade_registry_includes_noop_and_applies_identity()
    {
        Assert.Contains(
            SchemaUpgradeRegistry.RegisteredSteps,
            s => s.DocumentType == SchemaDocumentTypes.TestRunRecord
                 && s.FromVersion == 1
                 && s.ToVersion == 2
                 && s.Transform is null);

        var reached = SchemaUpgradeRegistry.Apply(SchemaDocumentTypes.TestRunRecord, fromVersion: 1, targetVersion: 2);
        Assert.Equal(2, reached);
    }

    [Fact]
    public async Task Dut_history_on_legacy_record_reports_no_comparison()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        await store.SaveAsync(new TestRunRecord
        {
            RunId = "prior",
            PlanId = "sample",
            PlanName = "Sample Hardware Suite",
            DutSerial = "SN-LEGACY",
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Result = RunResult.Passed,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    Value = 10,
                    HistoryEnabled = true,
                    Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
                },
            ],
        });

        var dest = Path.Combine(store.GetRunDirectory("legacy-run-1"), "run.json");
        File.Copy(FixturePath("run-v0-legacy.json"), dest, overwrite: true);
        var legacy = await store.LoadAsync("legacy-run-1");
        Assert.NotNull(legacy);
        Assert.True(legacy!.IsLegacy);

        // Shift value so a default-threshold path would have flagged Watch.
        legacy.Samples[0].Value = 9.0;
        var report = await new DutHistoryService(store).AnalyzeAsync(legacy);
        Assert.Contains("No comparison available", report.OperatorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Metrics);
        Assert.Equal(DutHistorySeverity.Normal, report.OverallSeverity);
    }
}
