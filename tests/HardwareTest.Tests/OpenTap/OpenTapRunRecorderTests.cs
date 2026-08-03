using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class OpenTapRunRecorderTests
{
    public const string SampleRecordingBaseName = "sample-pass";

    [Fact]
    public void Checked_in_sample_recording_has_progress_and_summary()
    {
        var dir = OpenTapRunRecorder.DefaultRecordingsDirectory;
        var recording = OpenTapRunRecorder.LoadBeside(dir, SampleRecordingBaseName);
        Assert.NotEmpty(recording.Progress);
        Assert.False(string.IsNullOrWhiteSpace(recording.Summary.RunId));
        Assert.Equal(RunResult.Passed, recording.Summary.Result);
        Assert.NotEmpty(recording.Summary.Steps);
    }

    [Fact]
    public async Task Recorder_captures_flat_leaves_run()
    {
        var session = new OpenTapSession();
        await session.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-REC", Family: "demo"));

        var recorder = new OpenTapRunRecorder();
        var summary = await session.RunAsync(recorder);
        Assert.True(
            summary.Result is RunResult.Passed or RunResult.Failed or RunResult.Error,
            $"Unexpected result {summary.Result}: {summary.ErrorMessage}");
        Assert.NotEmpty(recorder.Frames);

        var dir = Path.Combine(Path.GetTempPath(), "opentap-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            recorder.WriteBeside(dir, "flat-leaves", summary);
            var loaded = OpenTapRunRecorder.LoadBeside(dir, "flat-leaves");
            Assert.Equal(summary.RunId, loaded.Summary.RunId);
            Assert.Equal(recorder.Frames.Count, loaded.Progress.Count);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

#if RECORD_OPENTAP_RUN
    [Fact]
    public async Task Record_sample_pass_cassette()
    {
        var session = new OpenTapSession();
        await session.LoadSampleProgramAsync();
        await session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-RECORD", Family: "demo"));

        var recorder = new OpenTapRunRecorder();
        using var resume = new CancellationTokenSource();
        var runTask = session.RunAsync(recorder);
        _ = Task.Run(async () =>
        {
            while (!resume.IsCancellationRequested && !runTask.IsCompleted)
            {
                if (session.IsAwaitingOperator)
                {
                    session.Resume();
                }

                await Task.Delay(20);
            }
        });

        var summary = await runTask;
        resume.Cancel();
        Assert.Equal(RunResult.Passed, summary.Result);

        var dest = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "fixtures", "opentap", "recordings"));
        recorder.WriteBeside(dest, SampleRecordingBaseName, summary);
    }
#endif
}
