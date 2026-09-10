namespace HardwareTest.Core.StationHealth;

/// Well-known sidecar / catalog programKind values.
public static class ProgramKinds
{
    public const string Dut = "dut";
    public const string StationHealth = "stationHealth";

    public static bool IsStationHealth(string? kind)
        => string.Equals(kind, StationHealth, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnown(string? kind)
        => string.IsNullOrWhiteSpace(kind)
           || string.Equals(kind, Dut, StringComparison.OrdinalIgnoreCase)
           || IsStationHealth(kind);
}

/// Sidecar stationHealthGate values (enforced in Area 5).
public static class StationHealthGates
{
    public const string Warn = "warn";
    public const string Block = "block";

    public static bool IsKnown(string? gate)
        => string.IsNullOrWhiteSpace(gate)
           || string.Equals(gate, Warn, StringComparison.OrdinalIgnoreCase)
           || string.Equals(gate, Block, StringComparison.OrdinalIgnoreCase);
}

/// StationHealthRecord.source values.
public static class StationHealthSources
{
    public const string Queried = "queried";
    public const string Recalled = "recalled";
}

/// StationHealthRecord.verdict values.
public static class StationHealthVerdicts
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string Error = "Error";
}

/// One metric copied from a health-run Scalar.
public sealed class StationHealthMetric
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public string? Unit { get; set; }
    public double? LimitLow { get; set; }
    public double? LimitHigh { get; set; }
}

/// Station-scoped cal / health snapshot under `{DataDirectory}/station-health/{profileId}.json`.
public sealed class StationHealthRecord
{
    public int SchemaVersion { get; set; }
    public string ProfileId { get; set; } = "default";
    public DateTimeOffset MeasuredAt { get; set; }
    public string Source { get; set; } = StationHealthSources.Queried;
    public string Verdict { get; set; } = StationHealthVerdicts.Pass;
    public string? ProgramId { get; set; }
    public string? RunId { get; set; }
    public List<StationHealthMetric> Metrics { get; set; } = [];
    public double? MaxAgeHours { get; set; }
}
