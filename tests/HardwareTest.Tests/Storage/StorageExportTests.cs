using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Core.Time;
using Xunit;

namespace HardwareTest.Tests.Storage;

public sealed class StorageHealthServiceTests
{
    [Theory]
    [InlineData(3L * 1024 * 1024 * 1024, StorageHealthLevel.Ok)]
    [InlineData(1L * 1024 * 1024 * 1024, StorageHealthLevel.Warn)]
    [InlineData(100L * 1024 * 1024, StorageHealthLevel.Critical)]
    public void Levels_match_thresholds(long available, StorageHealthLevel expected)
    {
        var settings = new AppSettings
        {
            DataFreeSpaceWarnBytes = 2L * 1024 * 1024 * 1024,
            DataFreeSpaceCriticalBytes = 512L * 1024 * 1024,
        };
        var svc = new StorageHealthService(settings, Path.GetTempPath(), _ => available);
        var snap = svc.GetDataVolumeHealth();
        Assert.Equal(expected, snap.Level);
        Assert.Equal(available, snap.AvailableBytes);
    }
}

public sealed class RunRetentionServiceTests
{
    [Fact]
    public void Prune_deletes_old_completed_keeps_in_progress()
    {
        var root = Path.Combine(Path.GetTempPath(), "hwtest-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
            var clock = new HardwareTest.Tests.Time.FakeClock(now);
            var oldDir = Path.Combine(root, "old-run");
            var freshDir = Path.Combine(root, "fresh-run");
            var activeDir = Path.Combine(root, "active-run");
            WriteRun(oldDir, now.AddDays(-60), RunResult.Passed);
            WriteRun(freshDir, now.AddDays(-1), RunResult.Passed);
            WriteRun(activeDir, now, RunResult.Unknown);

            var settings = new AppSettings { RunRetentionDays = 30, RunRetentionMaxRuns = 500 };
            var svc = new RunRetentionService(settings, root, clock: clock);
            var result = svc.Prune(dryRun: true);
            Assert.Contains(oldDir, result.DeletedPaths);
            Assert.DoesNotContain(freshDir, result.DeletedPaths);
            Assert.Contains(activeDir, result.SkippedInProgress);

            result = svc.Prune(dryRun: false);
            Assert.False(Directory.Exists(oldDir));
            Assert.True(Directory.Exists(freshDir));
            Assert.True(Directory.Exists(activeDir));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Prune_enforces_max_count()
    {
        var root = Path.Combine(Path.GetTempPath(), "hwtest-retention-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
            var clock = new HardwareTest.Tests.Time.FakeClock(now);
            for (var i = 0; i < 5; i++)
            {
                WriteRun(
                    Path.Combine(root, $"run-{i}"),
                    now.AddHours(-i),
                    RunResult.Passed);
            }

            var settings = new AppSettings { RunRetentionDays = 0, RunRetentionMaxRuns = 2 };
            var svc = new RunRetentionService(settings, root, clock: clock);
            var result = svc.Prune();
            Assert.Equal(3, result.DeletedCount);
            Assert.Equal(2, Directory.GetDirectories(root).Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("run.json", "{}")]
    [InlineData("run.json", "{\"schemaVersion\":0}")]
    [InlineData("run.json", "{\"schemaVersion\":1}")]
    [InlineData("run.json", "{\"schemaVersion\":2}")]
    [InlineData("run.json", "{\"schemaVersion\":3}")]
    [InlineData("run.json", "{\"schemaVersion\":99}")]
    [InlineData("suite-run.json", "{}")]
    [InlineData("suite-run.json", "{\"schemaVersion\":0}")]
    [InlineData("suite-run.json", "{\"schemaVersion\":99}")]
    [InlineData("suite-run.json", "{\"schemaVersion\":1,\"planRuns\":[{}]}")]
    [InlineData("suite-run.json", "{\"schemaVersion\":1,\"planRuns\":[{\"schemaVersion\":99}]}")]
    public void Prune_protects_unsupported_and_future_documents(string fileName, string json)
    {
        using var temp = new HardwareTest.Tests.Fixtures.TempDataDirectory();
        var root = temp.RunsDirectory;
        var dir = fileName == "run.json" ? Path.Combine(root, "protected") : Path.Combine(root, "suites", "protected");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, json);
        Directory.SetCreationTimeUtc(dir, new DateTime(2020, 1, 1));
        WriteRun(Path.Combine(root, "eligible"), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), RunResult.Passed);
        var settings = new AppSettings { RunRetentionDays = 1, RunRetentionMaxRuns = 1 };
        var clock = new HardwareTest.Tests.Time.FakeClock(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        var result = new RunRetentionService(settings, root, clock: clock).Prune();
        Assert.DoesNotContain(dir, result.DeletedPaths);
        Assert.True(Directory.Exists(dir));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.Single(result.DeletedPaths);
    }

    private static void WriteRun(string dir, DateTimeOffset started, RunResult result)
    {
        Directory.CreateDirectory(dir);
        var record = new TestRunRecord
        {
            RunId = Path.GetFileName(dir),
            PlanName = "plan",
            StartedAt = started,
            CompletedAt = started.AddMinutes(1),
            Result = result,
            SchemaVersion = HardwareTest.Core.Serialization.SchemaVersions.TestRunRecord,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            record,
            HardwareTest.Core.Serialization.AppJsonContext.Default.TestRunRecord);
        File.WriteAllText(Path.Combine(dir, "run.json"), json);
    }
}

public sealed class ExportTargetServiceTests
{
    [Fact]
    public void WriteAtomic_and_ExportPackage_round_trip()
    {
        var root = Path.Combine(Path.GetTempPath(), "hwtest-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new AppSettings
            {
                ExportDirectory = root,
                PreferRemovableExport = false,
            };
            var svc = new ExportTargetService(settings, root, removableRoots: () => []);
            var targets = svc.ListTargets();
            Assert.Contains(targets, t => t.Id == "configured");
            var target = targets.First(t => t.Id == "configured");
            Assert.Equal($"Export directory — {Path.GetFullPath(root)}", target.DisplayName);
            Assert.DoesNotContain(targets, t => t.Id == "local-exports");

            var written = svc.WriteAtomic(target, "note.txt", "hello"u8.ToArray());
            Assert.True(File.Exists(written));
            Assert.Equal("hello", File.ReadAllText(written));

            var srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            var pdf = Path.Combine(srcDir, "status.pdf");
            var runJson = Path.Combine(srcDir, "run.json");
            File.WriteAllText(pdf, "pdf");
            File.WriteAllText(runJson, "{}");
            var package = svc.ExportPackage(
                target,
                "run-abc",
                [(pdf, "status.pdf"), (runJson, "run.json")]);
            Assert.True(File.Exists(Path.Combine(package, "status.pdf")));
            Assert.True(File.Exists(Path.Combine(package, "run.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ListTargets_prefer_removable_orders_volumes_before_configured()
    {
        var root = Path.Combine(Path.GetTempPath(), "hwtest-export-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stick = new ExportTarget
            {
                Id = "removable:stick",
                DisplayName = "Removable (stick) — /mnt/stick",
                RootPath = "/mnt/stick",
                IsRemovable = true,
            };
            var settings = new AppSettings
            {
                ExportDirectory = root,
                PreferRemovableExport = true,
            };
            var svc = new ExportTargetService(settings, root, removableRoots: () => [stick]);
            Assert.Equal(["removable:stick", "configured"], svc.ListTargets().Select(t => t.Id).ToArray());

            settings.PreferRemovableExport = false;
            Assert.Equal(["configured", "removable:stick"], svc.ListTargets().Select(t => t.Id).ToArray());
            Assert.DoesNotContain(svc.ListTargets(), t => t.Id == "local-exports");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ListTargets_local_exports_only_when_nothing_else_is_listed()
    {
        var data = Path.Combine(Path.GetTempPath(), "hwtest-export-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            var svc = new ExportTargetService(
                new AppSettings { ExportDirectory = string.Empty },
                data,
                removableRoots: () => []);
            var targets = svc.ListTargets();
            Assert.Single(targets);
            Assert.Equal("local-exports", targets[0].Id);
            Assert.Equal($"Local exports — {Path.Combine(data, "exports")}", targets[0].DisplayName);
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    [Fact]
    public void FormatDisplayName_windows_volume_does_not_repeat_the_drive_letter()
    {
        Assert.Equal("Removable — E:\\", ExportTargetService.FormatDisplayName("Removable", "E:\\"));
        Assert.Equal("Removable (stick) — /mnt/stick", ExportTargetService.FormatDisplayName("Removable (stick)", "/mnt/stick"));
    }
}
