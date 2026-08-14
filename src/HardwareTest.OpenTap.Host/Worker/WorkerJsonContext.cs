using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host.Worker;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WorkerEnvelope))]
[JsonSerializable(typeof(WorkerSnapshot))]
[JsonSerializable(typeof(WorkerInitRequest))]
[JsonSerializable(typeof(WorkerPathRequest))]
[JsonSerializable(typeof(WorkerFixtureRequest))]
[JsonSerializable(typeof(WorkerRunRequest))]
[JsonSerializable(typeof(WorkerRunSelectionRequest))]
[JsonSerializable(typeof(WorkerResumeRequest))]
[JsonSerializable(typeof(WorkerAbortRequest))]
[JsonSerializable(typeof(WorkerStationDutRequest))]
[JsonSerializable(typeof(WorkerSetEnabledRequest))]
[JsonSerializable(typeof(WorkerAcquireSettingsRequest))]
[JsonSerializable(typeof(WorkerMeanGteRequest))]
[JsonSerializable(typeof(WorkerResourceRequest))]
[JsonSerializable(typeof(WorkerBindSlotRequest))]
[JsonSerializable(typeof(WorkerEnumerateParametersRequest))]
[JsonSerializable(typeof(WorkerMemberKeyRequest))]
[JsonSerializable(typeof(WorkerBoolResult))]
[JsonSerializable(typeof(WorkerRunResult))]
[JsonSerializable(typeof(WorkerParameterListResult))]
[JsonSerializable(typeof(WorkerPluginDirectoryListResult))]
[JsonSerializable(typeof(WorkerPackageListResult))]
[JsonSerializable(typeof(WorkerDiscoveredAddressListResult))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(OpenTapStepNode))]
[JsonSerializable(typeof(List<OpenTapStepNode>))]
[JsonSerializable(typeof(OpenTapInstrumentSlot))]
[JsonSerializable(typeof(List<OpenTapInstrumentSlot>))]
[JsonSerializable(typeof(OpenTapProgress))]
[JsonSerializable(typeof(OpenTapRunSummary))]
[JsonSerializable(typeof(OpenTapParameterInfo))]
[JsonSerializable(typeof(List<OpenTapParameterInfo>))]
[JsonSerializable(typeof(OpenTapPluginDirectoryInfo))]
[JsonSerializable(typeof(List<OpenTapPluginDirectoryInfo>))]
[JsonSerializable(typeof(OpenTapPackageInfo))]
[JsonSerializable(typeof(List<OpenTapPackageInfo>))]
[JsonSerializable(typeof(OpenTapDiscoveredAddress))]
[JsonSerializable(typeof(List<OpenTapDiscoveredAddress>))]
[JsonSerializable(typeof(OperatorInteractionRequest))]
[JsonSerializable(typeof(OperatorInteractionResponse))]
[JsonSerializable(typeof(OperatorInteractionField))]
[JsonSerializable(typeof(List<OperatorInteractionField>))]
[JsonSerializable(typeof(MeasurementSampleEvent))]
[JsonSerializable(typeof(StoredSample))]
[JsonSerializable(typeof(List<StoredSample>))]
[JsonSerializable(typeof(StepResultRecord))]
[JsonSerializable(typeof(List<StepResultRecord>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]
public partial class WorkerJsonContext : JsonSerializerContext;
