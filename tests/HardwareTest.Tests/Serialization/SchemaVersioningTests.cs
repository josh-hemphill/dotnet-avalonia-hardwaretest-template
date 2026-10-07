using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Theory]
    [InlineData("run-v0-legacy.json", "legacy-run-1")]
    [InlineData("run-v1.json", "current-run-1")]
    public async Task Unsupported_run_fixture_rejects_load_and_overwrite_preserving_bytes(string fixture, string runId)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dest = Path.Combine(store.GetRunDirectory(runId), "run.json");
        File.Copy(FixturePath(fixture), dest);
        var before = File.ReadAllBytes(dest);

        var error = await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => store.LoadAsync(runId));
        Assert.Contains("requires schema 4", error.Message);
        Assert.Contains(dest, error.Message);
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => store.SaveAsync(new TestRunRecord { RunId = runId }));
        Assert.Equal(before, File.ReadAllBytes(dest));
        Assert.Empty(await store.ListAsync());
        Assert.False(File.Exists(dest + ".bak"));
    }

    [Fact]
    public async Task Future_run_fixture_loads_read_only_and_loaded_or_fresh_save_is_rejected()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var dest = Path.Combine(store.GetRunDirectory("future-run-1"), "run.json");
        File.Copy(FixturePath("run-v999-future.json"), dest);
        var before = File.ReadAllBytes(dest);

        var run = await store.LoadAsync("future-run-1");
        Assert.NotNull(run);
        Assert.True(run.IsSchemaReadOnly);
        Assert.Equal(999, run.StoredSchemaVersion);
        Assert.Equal("9.9.9+ffff.20990101000000", run.AppVersion);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(run));
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.SaveAsync(new TestRunRecord { RunId = run.RunId }));
        Assert.Equal(before, File.ReadAllBytes(dest));
        Assert.True(Assert.Single(await store.ListAsync()).IsSchemaReadOnly);
    }

    [Fact]
    public async Task Current_run_round_trip_preserves_explicit_schema_and_current_data()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var current = JsonNode.Parse(File.ReadAllBytes(FixturePath("run-v1.json")))!;
        current["schemaVersion"] = SchemaVersions.TestRunRecord;
        var dest = Path.Combine(store.GetRunDirectory("current-run-1"), "run.json");
        File.WriteAllText(dest, current.ToJsonString());

        var loaded = await store.LoadAsync("current-run-1");
        Assert.NotNull(loaded);
        Assert.Equal(SchemaVersions.TestRunRecord, loaded.SchemaVersion);
        Assert.Equal(SchemaVersions.TestRunRecord, loaded.StoredSchemaVersion);
        Assert.False(loaded.IsSchemaReadOnly);
        Assert.True(loaded.Samples[0].HistoryEnabled);
        Assert.Empty(loaded.Events);
        Assert.All(loaded.Samples, s => Assert.Null(s.ElapsedMs));
        await store.SaveAsync(loaded);
        using var document = JsonDocument.Parse(File.ReadAllBytes(dest));
        Assert.Equal(SchemaVersions.TestRunRecord, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task Run_writer_rejects_old_objects_without_stamping_or_creating_a_document(int version)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = new TestRunRecord { RunId = "unsupported", SchemaVersion = version };
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => store.SaveAsync(run));
        Assert.Equal(version, run.SchemaVersion);
        Assert.False(File.Exists(Path.Combine(store.GetRunDirectory(run.RunId), "run.json")));
    }

    [Theory]
    [InlineData(SchemaDocumentTypes.AppSettings, SchemaVersions.AppSettings)]
    [InlineData(SchemaDocumentTypes.UiState, SchemaVersions.UiState)]
    [InlineData(SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord)]
    [InlineData(SchemaDocumentTypes.SuiteRunRecord, SchemaVersions.SuiteRunRecord)]
    [InlineData(SchemaDocumentTypes.CrashReport, SchemaVersions.CrashReport)]
    [InlineData(SchemaDocumentTypes.StationHealthRecord, SchemaVersions.StationHealthRecord)]
    public void Header_requires_explicit_current_version_for_each_document_family(string kind, int current)
    {
        var status = DocumentSchemaGate.ReadHeader(Encoding.UTF8.GetBytes($"{{\"schemaVersion\":{current}}}"), kind, current);
        Assert.Equal(DocumentSchemaKind.Current, status.Kind);
        foreach (var json in new[] { "{}", "{\"schemaVersion\":0}", "{\"schemaVersion\":-1}", $"{{\"schemaVersion\":{current - 1}}}", $"{{\"SchemaVersion\":{current}}}" })
        {
            var error = Assert.Throws<UnsupportedDocumentSchemaException>(() => DocumentSchemaGate.ReadHeader(Encoding.UTF8.GetBytes(json), kind, current));
            Assert.Equal(current, error.Status.CurrentVersion);
            Assert.Equal(DocumentSchemaKind.Unsupported, error.Status.Kind);
        }
        var future = DocumentSchemaGate.ReadHeader(Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"appVersion\":\"newer-app\"}"), kind, current);
        Assert.Equal(DocumentSchemaKind.FutureReadOnly, future.Kind);
        Assert.Contains("newer-app", future.FormatOperatorWarning());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":\"4\"}")]
    [InlineData("{\"schemaVersion\":4.5}")]
    [InlineData("{\"schemaVersion\":2147483648}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{invalid")]
    public void Malformed_headers_are_errors_before_deserialization(string json)
    {
        Assert.ThrowsAny<JsonException>(() => DocumentSchemaGate.ReadHeader(Encoding.UTF8.GetBytes(json), SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord));
    }

    [Theory]
    [InlineData("settings-v0-legacy.json", false)]
    [InlineData("settings-v999-future.json", true)]
    public async Task Unsupported_and_future_settings_fixtures_preserve_bytes_and_refuse_save(string fixture, bool future)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.Copy(FixturePath(fixture), path);
        var before = File.ReadAllBytes(path);
        var warnings = new List<string>();
        var store = new SettingsStore(temp.Path);
        await store.LoadAsync(null, null, warnings.Add);
        Assert.False(store.IsSettingsWritable);
        Assert.False(string.IsNullOrWhiteSpace(store.SettingsSchemaWarning));
        Assert.Contains(warnings, w => w.Contains(future ? "Read-only" : "Unsupported", StringComparison.OrdinalIgnoreCase));
        store.AppSettings.ThemePreference = "Dark";
        await store.SaveAppSettingsAsync();
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Report_roles_require_explicit_working_or_issued_values()
    {
        Assert.True(ReportArtifactRoles.IsWorking(ReportArtifactRoles.Working));
        Assert.True(ReportArtifactRoles.IsIssued(ReportArtifactRoles.Issued));
        Assert.False(ReportArtifactRoles.IsWorking(null));
        Assert.False(ReportArtifactRoles.IsWorking(""));
        Assert.False(ReportArtifactRoles.IsWorking("custom-role"));
        Assert.False(ReportArtifactRoles.IsIssued(null));
    }
}
