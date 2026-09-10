using HardwareTest.Core.Time;

namespace HardwareTest.Core.StationHealth;

/// Sidecar / settings pin that changes enforcement when the DUT opted in.
public static class StationHealthGateOverrides
{
    public const string Off = "off";
    public const string Warn = "warn";
    public const string Block = "block";

    public static bool IsKnown(string? value)
        => string.IsNullOrWhiteSpace(value)
           || string.Equals(value, Off, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Warn, StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, Block, StringComparison.OrdinalIgnoreCase);
}

/// Evaluate result level. Off means the gate is inactive.
public static class StationHealthGateLevels
{
    public const string Off = "Off";
    public const string Ok = "Ok";
    public const string Warn = "Warn";
    public const string Block = "Block";
}

/// Catalog / sidecar inputs for one Evaluate call (Avalonia-free).
public sealed record StationHealthGateRequest
{
    public string? ProgramKind { get; init; }
    public bool RequireStationHealth { get; init; }
    public double? MaxAgeHours { get; init; }
    public string? Gate { get; init; }
    public string? ProfileId { get; init; }
}

/// Freshness decision for Run start and Settings chrome.
public sealed class StationHealthGateResult
{
    public string Level { get; init; } = StationHealthGateLevels.Off;
    public string Message { get; init; } = string.Empty;
    public TimeSpan? Age { get; init; }
    public string? Verdict { get; init; }
}

/// Station-scoped freshness gate. Health programs and sidecars without keys stay Off.
public interface IStationHealthGate
{
    StationHealthGateResult Evaluate(StationHealthGateRequest program, IClock clock);
}
