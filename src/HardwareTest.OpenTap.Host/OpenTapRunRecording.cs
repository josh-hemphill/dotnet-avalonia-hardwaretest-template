using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.Core.Runs;

namespace HardwareTest.OpenTap.Host;

/// Progress/summary cassette for offline UI/host assertions (not a full SCPI VCR).
public sealed record OpenTapRunRecording(
    IReadOnlyList<OpenTapProgressFrameDto> Progress,
    OpenTapRunSummaryDto Summary);

public sealed class OpenTapProgressFrameDto
{
    public string Message { get; set; } = string.Empty;
    public string? StepName { get; set; }
    public string? StepPath { get; set; }
    public string? StepId { get; set; }
    public string? Verdict { get; set; }
    public string? StatusText { get; set; }
    public string? KeyValue { get; set; }
    public double OverallPercent { get; set; }
    public bool IsCompleted { get; set; }
    public bool AwaitingOperator { get; set; }
    public string? OperatorPromptMessage { get; set; }
    public RunResult? Result { get; set; }
    public StoredSample? Sample { get; set; }

    public static OpenTapProgressFrameDto From(OpenTapProgress p) => new()
    {
        Message = p.Message,
        StepName = p.StepName,
        StepPath = p.StepPath,
        StepId = p.StepId,
        Verdict = p.Verdict,
        StatusText = p.StatusText,
        KeyValue = p.KeyValue,
        OverallPercent = p.OverallPercent,
        IsCompleted = p.IsCompleted,
        AwaitingOperator = p.AwaitingOperator,
        OperatorPromptMessage = p.OperatorPromptMessage,
        Result = p.Result,
        Sample = p.Sample is null
            ? null
            : new StoredSample
            {
                Channel = p.Sample.Channel,
                Timestamp = p.Sample.Timestamp,
                Value = p.Sample.Value,
            },
    };

    public OpenTapProgress ToProgress() => new()
    {
        Message = Message,
        StepName = StepName,
        StepPath = StepPath,
        StepId = StepId,
        Verdict = Verdict,
        StatusText = StatusText,
        KeyValue = KeyValue,
        OverallPercent = OverallPercent,
        IsCompleted = IsCompleted,
        AwaitingOperator = AwaitingOperator,
        OperatorPromptMessage = OperatorPromptMessage,
        Result = Result,
        Sample = Sample is null
            ? null
            : new MeasurementSampleEvent(Sample.Channel, 0, Sample.Value, Sample.Timestamp),
    };
}

public sealed class OpenTapRunSummaryDto
{
    public string RunId { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public RunResult Result { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DutSerial { get; set; }
    public string? DutPartNumber { get; set; }
    public string? DutRevision { get; set; }
    public string? SessionId { get; set; }
    public string? OperatorName { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public List<StoredSample> Samples { get; set; } = [];
    public List<StepResultRecord> Steps { get; set; } = [];
    public string Verdict { get; set; } = "NotSet";

    public static OpenTapRunSummaryDto From(OpenTapRunSummary s) => new()
    {
        RunId = s.RunId,
        PlanName = s.PlanName,
        Result = s.Result,
        ErrorMessage = s.ErrorMessage,
        DutSerial = s.DutSerial,
        DutPartNumber = s.DutPartNumber,
        DutRevision = s.DutRevision,
        SessionId = s.SessionId,
        OperatorName = s.OperatorName,
        StartedAt = s.StartedAt,
        CompletedAt = s.CompletedAt,
        Samples = s.Samples.ToList(),
        Steps = s.Steps.ToList(),
        Verdict = s.Verdict,
    };

    public OpenTapRunSummary ToSummary() => new()
    {
        RunId = RunId,
        PlanName = PlanName,
        Result = Result,
        ErrorMessage = ErrorMessage,
        DutSerial = DutSerial,
        DutPartNumber = DutPartNumber,
        DutRevision = DutRevision,
        SessionId = SessionId,
        OperatorName = OperatorName,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        Samples = Samples.ToList(),
        Steps = Steps.ToList(),
        Verdict = Verdict,
    };
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OpenTapProgressFrameDto))]
[JsonSerializable(typeof(List<OpenTapProgressFrameDto>))]
[JsonSerializable(typeof(OpenTapRunSummaryDto))]
[JsonSerializable(typeof(StoredSample))]
[JsonSerializable(typeof(List<StoredSample>))]
[JsonSerializable(typeof(StepResultRecord))]
[JsonSerializable(typeof(List<StepResultRecord>))]
public partial class OpenTapRecordingJsonContext : JsonSerializerContext;

/// Loads/writes progress+summary JSON cassettes beside a base name.
public static class OpenTapRunRecordingStore
{
    public static void WriteBeside(string directory, string baseName, IEnumerable<OpenTapProgressFrameDto> frames, OpenTapRunSummary summary)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{baseName}.progress.json"),
            JsonSerializer.Serialize(frames.ToList(), OpenTapRecordingJsonContext.Default.ListOpenTapProgressFrameDto));
        File.WriteAllText(
            Path.Combine(directory, $"{baseName}.summary.json"),
            JsonSerializer.Serialize(OpenTapRunSummaryDto.From(summary), OpenTapRecordingJsonContext.Default.OpenTapRunSummaryDto));
    }

    public static OpenTapRunRecording LoadBeside(string directory, string baseName)
    {
        var frames = JsonSerializer.Deserialize(
                         File.ReadAllText(Path.Combine(directory, $"{baseName}.progress.json")),
                         OpenTapRecordingJsonContext.Default.ListOpenTapProgressFrameDto)
                     ?? [];
        var summary = JsonSerializer.Deserialize(
                          File.ReadAllText(Path.Combine(directory, $"{baseName}.summary.json")),
                          OpenTapRecordingJsonContext.Default.OpenTapRunSummaryDto)
                      ?? throw new InvalidOperationException($"Missing summary for '{baseName}' in {directory}");
        return new OpenTapRunRecording(frames, summary);
    }
}
