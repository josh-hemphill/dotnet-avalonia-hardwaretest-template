using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.StationHealth;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Time;
using Xunit;

namespace HardwareTest.Tests.StationHealth;

public sealed class StationHealthStoreTests
{
    [Fact]
    public async Task File_store_round_trips_current_schema()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var record = new StationHealthRecord
        {
            ProfileId = "bench-a",
            MeasuredAt = clock.UtcNow,
            Source = StationHealthSources.Queried,
            Verdict = StationHealthVerdicts.Pass,
            ProgramId = "station-health",
            RunId = "run-1",
            Metrics =
            [
                new StationHealthMetric
                {
                    Name = StationHealthRecorder.OffsetMetric,
                    Value = 0.002,
                    Unit = "V",
                    LimitLow = -0.01,
                    LimitHigh = 0.01,
                },
            ],
            MaxAgeHours = 24,
        };

        await store.WriteAsync(record);
        var loaded = store.TryRead("bench-a");
        Assert.NotNull(loaded);
        Assert.Equal(SchemaVersions.StationHealthRecord, loaded!.SchemaVersion);
        Assert.Equal("bench-a", loaded.ProfileId);
        Assert.Equal(clock.UtcNow, loaded.MeasuredAt);
        Assert.Equal(StationHealthSources.Queried, loaded.Source);
        Assert.Single(loaded.Metrics);
        Assert.Equal(0.002, loaded.Metrics[0].Value);
        Assert.True(File.Exists(store.PathFor("bench-a")));
    }

    [Fact]
    public void TryRead_missing_profile_returns_null()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        Assert.Null(store.TryRead("missing"));
    }

    [Fact]
    public async Task V1_fixture_deserializes()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var dest = store.PathFor("default");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "schema", "station-health-v1.json"),
            dest,
            overwrite: true);

        var loaded = store.TryRead("default");
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.SchemaVersion);
        Assert.Equal("queried", loaded.Source);
        Assert.Equal(2, loaded.Metrics.Count);
        Assert.Equal(24, loaded.MaxAgeHours);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"SchemaVersion\":1}")]
    public async Task Unsupported_header_is_rejected_and_cannot_be_overwritten(string json)
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var path = store.PathFor("default");
        File.WriteAllText(path, json);
        File.WriteAllText(path + ".bak", "{\"schemaVersion\":1}");
        Assert.Null(store.TryRead("default"));
        await Assert.ThrowsAsync<UnsupportedDocumentSchemaException>(
            () => store.WriteAsync(new StationHealthRecord()));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Equal("{\"schemaVersion\":1}", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public async Task Future_record_is_visible_but_fresh_current_record_cannot_overwrite_it()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var path = store.PathFor("default");
        const string json = "{\"schemaVersion\":99,\"profileId\":\"default\"}";
        File.WriteAllText(path, json);
        Assert.Equal(99, store.TryRead("default")!.SchemaVersion);
        await Assert.ThrowsAsync<SchemaReadOnlyException>(() => store.WriteAsync(new StationHealthRecord()));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task Writer_does_not_stamp_rejected_candidate(int version)
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var record = new StationHealthRecord { SchemaVersion = version };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => store.WriteAsync(record));
        Assert.Equal(version, record.SchemaVersion);
        Assert.False(File.Exists(store.PathFor("default")));
    }

    [Fact]
    public async Task Corrupt_primary_recovers_current_backup()
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        await store.WriteAsync(new StationHealthRecord { RunId = "first" });
        await store.WriteAsync(new StationHealthRecord { RunId = "second" });
        var path = store.PathFor("default");
        var backup = File.ReadAllBytes(path + ".bak");
        File.WriteAllText(path, "{broken");
        Assert.Equal("first", store.TryRead("default")!.RunId);
        Assert.Equal(backup, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Corrupt_primary_does_not_recover_noncurrent_backup(int version)
    {
        using var temp = new TempDataDirectory();
        var store = new FileStationHealthStore(temp.Path);
        var path = store.PathFor("default");
        const string primary = "{broken";
        var backup = $"{{\"schemaVersion\":{version}}}";
        File.WriteAllText(path, primary);
        File.WriteAllText(path + ".bak", backup);
        Assert.Null(store.TryRead("default"));
        Assert.Equal(primary, File.ReadAllText(path));
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Recorder_uses_clock_and_result_source()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero));
        var samples = new List<StoredSample>
        {
            new()
            {
                MetricKey = StationHealthRecorder.OffsetMetric,
                Value = 0.002,
                Unit = "V",
                LimitLow = -0.01,
                LimitHigh = 0.01,
                ResultSource = SampleResultSources.Cached,
            },
            new()
            {
                MetricKey = StationHealthRecorder.AgeMetric,
                Value = 1.5,
                Unit = "h",
                LimitHigh = 24,
                ResultSource = SampleResultSources.Cached,
            },
        };

        var record = StationHealthRecorder.Create(
            "default",
            "station-health",
            "run-9",
            RunResult.Passed,
            samples,
            clock);

        Assert.Equal(clock.UtcNow, record.MeasuredAt);
        Assert.Equal(StationHealthSources.Recalled, record.Source);
        Assert.Equal(StationHealthVerdicts.Pass, record.Verdict);
        Assert.Equal(24, record.MaxAgeHours);
        Assert.False(StationHealthRecorder.ShouldPersist(ProgramKinds.Dut, RunResult.Passed, samples));
        Assert.False(StationHealthRecorder.ShouldPersist(ProgramKinds.StationHealth, RunResult.Cancelled, samples));
        Assert.True(StationHealthRecorder.ShouldPersist(ProgramKinds.StationHealth, RunResult.Failed, samples));
        Assert.False(StationHealthRecorder.ShouldPersist(ProgramKinds.StationHealth, RunResult.Passed, []));
        Assert.False(StationHealthRecorder.ShouldPersist(
            ProgramKinds.StationHealth,
            RunResult.Passed,
            [new StoredSample { MetricKey = "VDC", Value = 1.25 }]));
    }
}
