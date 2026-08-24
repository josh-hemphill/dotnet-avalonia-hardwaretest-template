using System.Text.Json.Serialization;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(UiState))]
[JsonSerializable(typeof(VisaInstrument))]
[JsonSerializable(typeof(List<VisaInstrument>))]
[JsonSerializable(typeof(StationBinding))]
[JsonSerializable(typeof(List<StationBinding>))]
[JsonSerializable(typeof(PlanSlotOverride))]
[JsonSerializable(typeof(List<PlanSlotOverride>))]
[JsonSerializable(typeof(PlanParameterOverride))]
[JsonSerializable(typeof(List<PlanParameterOverride>))]
[JsonSerializable(typeof(TestRunRecord))]
[JsonSerializable(typeof(SuiteRunRecord))]
[JsonSerializable(typeof(StepResultRecord))]
[JsonSerializable(typeof(StepAttemptSummary))]
[JsonSerializable(typeof(StoredSample))]
[JsonSerializable(typeof(RunReportArtifact))]
[JsonSerializable(typeof(List<TestRunRecord>))]
[JsonSerializable(typeof(List<StepResultRecord>))]
[JsonSerializable(typeof(List<StepAttemptSummary>))]
[JsonSerializable(typeof(List<StoredSample>))]
[JsonSerializable(typeof(List<RunReportArtifact>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(CrashReportDocument))]
[JsonSerializable(typeof(CrashExceptionFrame))]
[JsonSerializable(typeof(List<CrashExceptionFrame>))]
[JsonSerializable(typeof(CrashSessionSnapshot))]
[JsonSerializable(typeof(CrashConfigSnapshot))]
[JsonSerializable(typeof(CrashConfigProvenanceRow))]
[JsonSerializable(typeof(List<CrashConfigProvenanceRow>))]
[JsonSerializable(typeof(ClockLastGoodRecord))]
[JsonSerializable(typeof(StationIdnDocument))]
[JsonSerializable(typeof(StationIdnRecord))]
[JsonSerializable(typeof(List<StationIdnRecord>))]
public partial class AppJsonContext : JsonSerializerContext;
