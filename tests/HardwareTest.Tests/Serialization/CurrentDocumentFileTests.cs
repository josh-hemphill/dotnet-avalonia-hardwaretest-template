using System.Text.Json;
using HardwareTest.Core.Crash;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Tests.Fixtures;
using Xunit;

namespace HardwareTest.Tests.Serialization;

public sealed class CurrentDocumentFileTests
{
    private static Task<(TestRunRecord? Document, DocumentSchemaStatus Status)> Read(string path)
        => CurrentDocumentFile.ReadAsync(path, AppJsonContext.Default.TestRunRecord, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord);

    private static Task Write(string path, TestRunRecord run)
        => CurrentDocumentFile.WriteAsync(path, run, AppJsonContext.Default.TestRunRecord, SchemaDocumentTypes.TestRunRecord, run.SchemaVersion, SchemaVersions.TestRunRecord);

    private static byte[] CurrentBytes(string id = "backup")
        => JsonSerializer.SerializeToUtf8Bytes(new TestRunRecord { RunId = id }, AppJsonContext.Default.TestRunRecord);

    [Theory]
    [InlineData("{invalid-json")]
    [InlineData("{\"schemaVersion\":4,\"samples\":{}}")]
    public async Task Corrupt_primary_recovers_only_valid_current_backup_atomically(string primary)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        var backup = CurrentBytes();
        File.WriteAllText(path, primary);
        File.WriteAllBytes(path + ".bak", backup);

        var loaded = await Read(path);
        Assert.Equal("backup", loaded.Document?.RunId);
        Assert.Equal(DocumentSchemaKind.Current, loaded.Status.Kind);
        Assert.Equal(backup, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
    }

    [Fact]
    public async Task Absent_primary_recovers_a_current_backup()
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        var backup = CurrentBytes();
        File.WriteAllBytes(path + ".bak", backup);
        var loaded = await Read(path);
        Assert.Equal("backup", loaded.Document?.RunId);
        Assert.Equal(backup, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":3}")]
    public async Task Unsupported_primary_is_never_replaced_by_current_backup(string primary)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        File.WriteAllText(path, primary);
        var before = File.ReadAllBytes(path);
        var backup = CurrentBytes();
        File.WriteAllBytes(path + ".bak", backup);
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => Read(path));
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => Write(path, new TestRunRecord { RunId = "replacement" }));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public async Task Future_primary_is_read_only_and_is_never_replaced_by_current_backup()
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        File.WriteAllText(path, "{\"schemaVersion\":999,\"runId\":\"future\",\"appVersion\":\"newer\"}");
        var before = File.ReadAllBytes(path);
        var backup = CurrentBytes();
        File.WriteAllBytes(path + ".bak", backup);
        var loaded = await Read(path);
        Assert.Equal("future", loaded.Document?.RunId);
        Assert.Equal(999, loaded.Document?.SchemaVersion);
        Assert.True(loaded.Status.IsReadOnly);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => Write(path, new TestRunRecord { RunId = "replacement" }));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
    }

    [Theory]
    [InlineData("{}", "unsupported")]
    [InlineData("{\"schemaVersion\":3}", "unsupported")]
    [InlineData("{\"schemaVersion\":999}", "future")]
    [InlineData("{invalid", "corrupt")]
    [InlineData("{\"schemaVersion\":4,\"samples\":{}}", "corrupt")]
    public async Task Recovery_rejects_noncurrent_or_invalid_backups_without_rewriting(string backup, string failure)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        File.WriteAllText(path, "{corrupt-primary");
        File.WriteAllText(path + ".bak", backup);
        var primaryBytes = File.ReadAllBytes(path);
        var backupBytes = File.ReadAllBytes(path + ".bak");
        if (failure == "unsupported") await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(() => Read(path));
        else if (failure == "future") await Assert.ThrowsAsync<SchemaReadOnlyException>(() => Read(path));
        else await Assert.ThrowsAnyAsync<JsonException>(() => Read(path));
        Assert.Equal(primaryBytes, File.ReadAllBytes(path));
        Assert.Equal(backupBytes, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public async Task Current_write_preserves_current_preimage_then_round_trips_new_document()
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        await Write(path, new TestRunRecord { RunId = "first" });
        Assert.False(File.Exists(path + ".bak"));
        var first = File.ReadAllBytes(path);
        await Write(path, new TestRunRecord { RunId = "second" });
        Assert.Equal(first, File.ReadAllBytes(path + ".bak"));
        Assert.Equal("second", (await Read(path)).Document?.RunId);
        Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"schemaVersion\":\"1\"}", false)]
    public async Task Crash_initializer_cannot_mask_missing_or_invalid_persisted_header(string json, bool unsupported)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "crash.json");
        Assert.Equal(SchemaVersions.CrashReport, new CrashReportDocument().SchemaVersion);
        File.WriteAllText(path, json);
        var before = File.ReadAllBytes(path);
        Task ReadCrash() => CurrentDocumentFile.ReadAsync(path, AppJsonContext.Default.CrashReportDocument, SchemaDocumentTypes.CrashReport, SchemaVersions.CrashReport);
        if (unsupported) await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(ReadCrash);
        else await Assert.ThrowsAnyAsync<JsonException>(ReadCrash);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"schemaVersion\":3}", false)]
    [InlineData("{\"schemaVersion\":999}", true)]
    public async Task Sole_noncurrent_backup_blocks_preflight_and_repeated_writes_without_creating_primary(string backup, bool future)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        File.WriteAllText(path + ".bak", backup);
        var before = File.ReadAllBytes(path + ".bak");
        var candidate = new TestRunRecord { RunId = "replacement" };
        Func<Task>[] actions =
        [
            () => CurrentDocumentFile.ValidateWriteDestinationAsync(path, AppJsonContext.Default.TestRunRecord,
                SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord),
            () => Write(path, candidate),
            () => Write(path, candidate),
        ];
        foreach (var action in actions)
        {
            if (future) await Assert.ThrowsAsync<SchemaReadOnlyException>(action);
            else await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(action);
            Assert.False(File.Exists(path));
            Assert.Equal(before, File.ReadAllBytes(path + ".bak"));
        }
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"schemaVersion\":4,\"samples\":{}}")]
    public async Task Sole_corrupt_backup_blocks_preflight_and_write_without_replacing_evidence(string backup)
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        File.WriteAllText(path + ".bak", backup);
        var before = File.ReadAllBytes(path + ".bak");
        await Assert.ThrowsAnyAsync<JsonException>(() => CurrentDocumentFile.ValidateWriteDestinationAsync(path,
            AppJsonContext.Default.TestRunRecord, SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord));
        await Assert.ThrowsAnyAsync<JsonException>(() => Write(path, new TestRunRecord { RunId = "replacement" }));
        Assert.False(File.Exists(path));
        Assert.Equal(before, File.ReadAllBytes(path + ".bak"));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Sole_current_backup_passes_preflight_without_recovery_and_remains_current_on_successful_saves()
    {
        using var temp = new TempDataDirectory();
        var path = Path.Combine(temp.Path, "run.json");
        var backup = CurrentBytes("preserved");
        File.WriteAllBytes(path + ".bak", backup);
        await CurrentDocumentFile.ValidateWriteDestinationAsync(path, AppJsonContext.Default.TestRunRecord,
            SchemaDocumentTypes.TestRunRecord, SchemaVersions.TestRunRecord);
        Assert.False(File.Exists(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        await Write(path, new TestRunRecord { RunId = "first" });
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        var first = File.ReadAllBytes(path);
        await Write(path, new TestRunRecord { RunId = "second" });
        Assert.Equal(first, File.ReadAllBytes(path + ".bak"));
        Assert.Equal("second", (await Read(path)).Document?.RunId);
    }

}
