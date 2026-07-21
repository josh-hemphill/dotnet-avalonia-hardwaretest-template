using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Tests.OpenTap;

/// Wraps IProgress and writes *.progress.json + *.summary.json cassettes.
public sealed class OpenTapRunRecorder : IProgress<OpenTapProgress>
{
    private readonly List<OpenTapProgressFrameDto> _frames = [];

    public IReadOnlyList<OpenTapProgressFrameDto> Frames => _frames;

    public void Report(OpenTapProgress value)
        => _frames.Add(OpenTapProgressFrameDto.From(value));

    public void WriteBeside(string directory, string baseName, OpenTapRunSummary summary)
        => OpenTapRunRecordingStore.WriteBeside(directory, baseName, _frames, summary);

    public static OpenTapRunRecording LoadBeside(string directory, string baseName)
        => OpenTapRunRecordingStore.LoadBeside(directory, baseName);

    public static string DefaultRecordingsDirectory
    {
        get
        {
            var fromOutput = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fixtures", "opentap", "recordings"));
            if (Directory.Exists(fromOutput))
            {
                return fromOutput;
            }

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "fixtures", "opentap", "recordings"));
        }
    }
}
