using System.Text.Json.Serialization;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;

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
[JsonSerializable(typeof(TestRunRecord))]
[JsonSerializable(typeof(SuiteRunRecord))]
[JsonSerializable(typeof(StepResultRecord))]
[JsonSerializable(typeof(StoredSample))]
[JsonSerializable(typeof(List<TestRunRecord>))]
[JsonSerializable(typeof(List<StepResultRecord>))]
[JsonSerializable(typeof(List<StoredSample>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class AppJsonContext : JsonSerializerContext;
