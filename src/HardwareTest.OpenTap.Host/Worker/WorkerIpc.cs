using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host.Worker;

/// Newline-delimited JSON on stdout; logs on stderr. Every protocol line is prefixed.
public static class WorkerProtocol
{
    public const string LinePrefix = "__htw__";
    public const int SchemaVersion = 1;
    public const string KindRequest = "request";
    public const string KindResponse = "response";
    public const string KindEvent = "event";

    public const string Init = "init";
    public const string ApplySettings = "applySettings";
    public const string Ping = "ping";
    public const string Shutdown = "shutdown";
    public const string LoadPlan = "loadPlan";
    public const string LoadSample = "loadSample";
    public const string LoadBoardDemo = "loadBoardDemo";
    public const string LoadSweepDemo = "loadSweepDemo";
    public const string LoadTimingDemo = "loadTimingDemo";
    public const string LoadPlanShape = "loadPlanShape";
    public const string TrySetStepEnabled = "trySetStepEnabled";
    public const string TryGetStepConditionSummary = "tryGetStepConditionSummary";
    public const string Run = "run";
    public const string RunSelection = "runSelection";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Abort = "abort";
    public const string ApplyStationAndDut = "applyStationAndDut";
    public const string TrySetAcquireSettings = "trySetAcquireSettings";
    public const string TrySetMeanGteThreshold = "trySetMeanGteThreshold";
    public const string TryRebindDmmResource = "tryRebindDmmResource";
    public const string TryBindSlotResource = "tryBindSlotResource";
    public const string EnumerateParameters = "enumerateParameters";
    public const string TryGetParameter = "tryGetParameter";
    public const string TrySetParameter = "trySetParameter";
    public const string ListPluginDirectories = "listPluginDirectories";
    public const string ListInstalledPackages = "listInstalledPackages";
    public const string ListDiscoveredDeviceAddresses = "listDiscoveredDeviceAddresses";
    public const string Progress = "progress";

    public static string FormatLine(WorkerEnvelope envelope)
        => LinePrefix + JsonSerializer.Serialize(envelope, WorkerJsonContext.Default.WorkerEnvelope);

    public static bool TryParseLine(string line, out WorkerEnvelope envelope)
    {
        envelope = null!;
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(LinePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var json = line[LinePrefix.Length..];
        var parsed = JsonSerializer.Deserialize(json, WorkerJsonContext.Default.WorkerEnvelope);
        if (parsed is null)
        {
            return false;
        }

        envelope = parsed;
        return true;
    }

    public static JsonElement SerializePayload<T>(T value, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.SerializeToElement(value, typeInfo);

    public static T? ReadPayload<T>(WorkerEnvelope envelope, JsonTypeInfo<T> typeInfo)
    {
        if (envelope.Payload is not { } payload
            || payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        return payload.Deserialize(typeInfo);
    }
}

public sealed class WorkerEnvelope
{
    public long Id { get; set; }
    public string Kind { get; set; } = WorkerProtocol.KindRequest;
    public string Method { get; set; } = string.Empty;
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    public JsonElement? Payload { get; set; }
}

public sealed class WorkerSnapshot
{
    public string? LoadedPlanPath { get; set; }
    public string? LoadedPlanName { get; set; }
    public List<OpenTapStepNode> StepTree { get; set; } = [];
    public List<OpenTapInstrumentSlot> InstrumentSlots { get; set; } = [];
    public bool IsExecuting { get; set; }
    public bool IsAwaitingOperator { get; set; }
    public string? OperatorPromptMessage { get; set; }
    public OperatorInteractionRequest? PendingInteraction { get; set; }

    public static WorkerSnapshot Capture(OpenTapSession session)
        => new()
        {
            LoadedPlanPath = session.LoadedPlanPath,
            LoadedPlanName = session.LoadedPlanName,
            StepTree = CloneTree(session.StepTree),
            InstrumentSlots = session.InstrumentSlots.Select(CloneSlot).ToList(),
            IsExecuting = session.IsExecuting,
            IsAwaitingOperator = session.IsAwaitingOperator,
            OperatorPromptMessage = session.OperatorPromptMessage,
            PendingInteraction = session.PendingInteraction,
        };

    private static List<OpenTapStepNode> CloneTree(IReadOnlyList<OpenTapStepNode> nodes)
        => nodes.Select(CloneNode).ToList();

    private static OpenTapStepNode CloneNode(OpenTapStepNode node)
        => new()
        {
            Id = node.Id,
            Name = node.Name,
            Path = node.Path,
            Enabled = node.Enabled,
            Verdict = node.Verdict,
            StatusText = node.StatusText,
            KeyValue = node.KeyValue,
            IsStage = node.IsStage,
            Children = CloneTree(node.Children),
        };

    private static OpenTapInstrumentSlot CloneSlot(OpenTapInstrumentSlot slot)
        => new()
        {
            Name = slot.Name,
            TypeName = slot.TypeName,
            RoleHint = slot.RoleHint,
            ResourceName = slot.ResourceName,
        };
}

public sealed class WorkerInitRequest
{
    public int ProtocolVersion { get; set; } = WorkerProtocol.SchemaVersion;
    public AppSettings Settings { get; set; } = new();
}

public sealed class WorkerPathRequest
{
    public string Path { get; set; } = string.Empty;
}

public sealed class WorkerFixtureRequest
{
    public string FixtureFileName { get; set; } = string.Empty;
}

public sealed class WorkerRunRequest
{
    public string? RunId { get; set; }
}

public sealed class WorkerRunSelectionRequest
{
    public string StepPath { get; set; } = string.Empty;
    public bool IncludeCleanup { get; set; } = true;
    public string? RunId { get; set; }
}

public sealed class WorkerResumeRequest
{
    public OperatorInteractionResponse? Response { get; set; }
}

public sealed class WorkerAbortRequest
{
    public bool SafetyStop { get; set; }
}

public sealed class WorkerStationDutRequest
{
    public Dictionary<string, string> RoleToResource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Serial { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public string? Revision { get; set; }
    public string Family { get; set; } = "generic";
}

public sealed class WorkerSetEnabledRequest
{
    public string StepPath { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class WorkerAcquireSettingsRequest
{
    public string StepPath { get; set; } = string.Empty;
    public int? SampleCount { get; set; }
    public int? IntervalMs { get; set; }
}

public sealed class WorkerMeanGteRequest
{
    public string StepPath { get; set; } = string.Empty;
    public double Threshold { get; set; }
}

public sealed class WorkerResourceRequest
{
    public string Resource { get; set; } = string.Empty;
}

public sealed class WorkerBindSlotRequest
{
    public string SlotName { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
}

public sealed class WorkerEnumerateParametersRequest
{
    public OpenTapParameterScope Scope { get; set; }
    public string? StepPath { get; set; }
    public bool IncludeReadOnly { get; set; }
    public OpenTapParameterListing Listing { get; set; } = OpenTapParameterListing.StationOverrides;
}

public sealed class WorkerMemberKeyRequest
{
    public string MemberKey { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class WorkerBoolResult
{
    public bool Ok { get; set; }
    public string? Value { get; set; }
    public WorkerSnapshot? Snapshot { get; set; }
}

public sealed class WorkerRunResult
{
    public OpenTapRunSummary Summary { get; set; } = new()
    {
        RunId = string.Empty,
        PlanName = string.Empty,
        Result = Core.Runs.RunResult.Cancelled,
    };

    public WorkerSnapshot Snapshot { get; set; } = new();
}

public sealed class WorkerParameterListResult
{
    public List<OpenTapParameterInfo> Items { get; set; } = [];
}

public sealed class WorkerPluginDirectoryListResult
{
    public List<OpenTapPluginDirectoryInfo> Items { get; set; } = [];
}

public sealed class WorkerPackageListResult
{
    public List<OpenTapPackageInfo> Items { get; set; } = [];
}

public sealed class WorkerDiscoveredAddressListResult
{
    public List<OpenTapDiscoveredAddress> Items { get; set; } = [];
}
