using HardwareTest.Core.Crash;
using HardwareTest.Core.Diagnostics;
using HardwareTest.Core.Logging;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using Serilog;
using Serilog.Events;
using Xunit;

namespace HardwareTest.Tests.Crash;

public sealed class CrashDossierWriterTests
{
    [Fact]
    public void Writer_produces_valid_crash_json_from_nested_exception()
    {
        using var temp = new TempDataDirectory();
        var writer = new CrashDossierWriter(Path.Combine(temp.Path, "crashes"), retentionCount: 20, redactIdentifiers: true);
        var inner = new InvalidOperationException("inner-fail");
        var outer = new Exception("outer-fail SN-SECRET", inner);
        var report = CrashDossierWriter.BuildReport(
            outer,
            isFatal: true,
            source: "test",
            SafeStopOutcome.NotAttempted,
            BuildInfo.FromAssembly(typeof(CrashDossierWriter).Assembly),
            activeRunId: "run1",
            activePlanId: "plan1",
            redact: true,
            "SN-SECRET");

        var dir = writer.TryWrite(new CrashCaptureContext
        {
            Report = report,
            Config = new CrashConfigSnapshot
            {
                Provenance =
                [
                    new CrashConfigProvenanceRow
                    {
                        Key = "DataDirectory",
                        EffectiveValue = temp.Path,
                        Source = "Default",
                    },
                ],
            },
            Session = CrashDossierWriter.BuildSessionSnapshot(true, "plan1", false, "SN-SECRET", "Tech", redact: true),
            LogTail = "line with SN-SECRET",
        });

        Assert.NotNull(dir);
        Assert.True(File.Exists(Path.Combine(dir!, "crash.json")));
        Assert.True(File.Exists(Path.Combine(dir!, "log-tail.txt")));
        Assert.True(File.Exists(Path.Combine(dir!, "config.json")));
        Assert.True(File.Exists(Path.Combine(dir!, "session.json")));

        using var stream = File.OpenRead(Path.Combine(dir!, "crash.json"));
        var loaded = System.Text.Json.JsonSerializer.Deserialize(stream, AppJsonContext.Default.CrashReportDocument);
        Assert.NotNull(loaded);
        Assert.Equal(SchemaVersions.CrashReport, loaded!.SchemaVersion);
        Assert.True(loaded.IsFatal);
        Assert.Equal(2, loaded.Exceptions.Count);
        Assert.Equal("run1", loaded.ActiveRunId);
    }

    [Fact]
    public void Reentrancy_second_write_while_busy_returns_null()
    {
        using var temp = new TempDataDirectory();
        var writer = new CrashDossierWriter(Path.Combine(temp.Path, "crashes"), redactIdentifiers: false);
        Assert.True(CrashDossierWriter.TryEnterWriteGateForTests());
        try
        {
            var nested = writer.TryWrite(new CrashCaptureContext
            {
                Report = CrashDossierWriter.BuildReport(
                    new Exception("nested"),
                    false,
                    "nested",
                    SafeStopOutcome.NotAttempted,
                    null,
                    null,
                    null,
                    false),
            });
            Assert.Null(nested);
        }
        finally
        {
            CrashDossierWriter.ExitWriteGateForTests();
        }

        var ok = writer.TryWrite(new CrashCaptureContext
        {
            Report = CrashDossierWriter.BuildReport(
                new Exception("ok"),
                false,
                "ok",
                SafeStopOutcome.NotAttempted,
                null,
                null,
                null,
                false),
        });
        Assert.NotNull(ok);
    }

    [Fact]
    public void Retention_prunes_to_configured_count()
    {
        using var temp = new TempDataDirectory();
        var writer = new CrashDossierWriter(Path.Combine(temp.Path, "crashes"), retentionCount: 2, redactIdentifiers: false);
        for (var i = 0; i < 5; i++)
        {
            var report = CrashDossierWriter.BuildReport(
                new Exception($"e{i}"),
                false,
                "retention",
                SafeStopOutcome.NotAttempted,
                null,
                null,
                null,
                false);
            report.CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(i);
            report.DossierId = $"id{i:D2}";
            Assert.NotNull(writer.TryWrite(new CrashCaptureContext { Report = report }));
            Thread.Sleep(15);
        }

        var remaining = Directory.GetDirectories(writer.CrashRoot);
        Assert.Equal(2, remaining.Length);
    }

    [Fact]
    public void Redaction_keeps_dut_serial_out_of_dossier_files()
    {
        using var temp = new TempDataDirectory();
        var serial = "SN-REDACT-ME-999";
        var writer = new CrashDossierWriter(Path.Combine(temp.Path, "crashes"), redactIdentifiers: true);
        var report = CrashDossierWriter.BuildReport(
            new Exception($"failed for {serial}"),
            true,
            "redact",
            SafeStopOutcome.Confirmed,
            null,
            null,
            null,
            redact: true,
            serial);
        var dir = writer.TryWrite(new CrashCaptureContext
        {
            Report = report,
            Session = CrashDossierWriter.BuildSessionSnapshot(true, "p", false, serial, "Op", true),
            Config = CrashDossierWriter.BuildConfigSnapshot(
            [
                new SettingProvenance
                {
                    Key = "Note",
                    EffectiveValue = serial,
                    Source = SettingSource.Default,
                },
            ],
            true,
            serial),
            LogTail = $"log {serial}",
            IdentifiersToRedact = [serial],
        });

        Assert.NotNull(dir);
        foreach (var file in Directory.EnumerateFiles(dir!))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(serial, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed class RingBufferSinkTests
{
    [Fact]
    public void Ring_sink_returns_entries_in_order_and_stays_bounded()
    {
        var sink = new RingBufferSink(capacity: 8);
        using var log = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        for (var i = 0; i < 20; i++)
        {
            log.Information("msg-{Index}", i);
        }

        Assert.True(sink.Count <= 8);
        var text = sink.DrainText();
        Assert.Contains("msg-19", text, StringComparison.Ordinal);
        Assert.DoesNotContain("msg-0", text, StringComparison.Ordinal);
        var idx12 = text.IndexOf("msg-12", StringComparison.Ordinal);
        var idx19 = text.IndexOf("msg-19", StringComparison.Ordinal);
        Assert.True(idx12 >= 0 && idx19 > idx12);
    }
}

public sealed class DanglingRunReconcilerTests
{
    [Fact]
    public async Task Reconciliation_converts_running_stub_to_aborted()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        await store.SaveAsync(new TestRunRecord
        {
            RunId = "dangling-1",
            PlanName = "Plan",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Result = RunResult.Unknown,
            SchemaVersion = SchemaVersions.TestRunRecord,
        });

        var count = await new DanglingRunReconciler(store).ReconcileAsync("abc12345");
        Assert.Equal(1, count);
        var loaded = await store.LoadAsync("dangling-1");
        Assert.NotNull(loaded);
        Assert.Equal(RunResult.Cancelled, loaded!.Result);
        Assert.NotNull(loaded.CompletedAt);
        Assert.Contains(DanglingRunReconciler.ProcessInterruptedReason, loaded.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("abc12345", loaded.ErrorMessage, StringComparison.Ordinal);
    }
}
