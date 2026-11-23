using System.Globalization;
using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Bring Up Rail Through Matrix", Groups: ["HardwareTest Examples"])]
public sealed class RailBringUpStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("groundLeak", "mA"), ("voltage", "V"), ("setpointError", "V"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "V", "mA");
        RequireLevels(row, 1, 1);
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var routes = fixture.ActiveRoutes;
        var nominal = row.Levels[0];
        // The simulator models a low-current preflight followed by enabling this rail and routing it to the DMM.
        evidence.Evaluate("groundLeak", routes[1].Nominal, "mA");
        if (evidence.Passed)
            fixture.EnableSupply();
        var samples = Enumerable.Range(0, 5).Select(i => fixture.SupplyEnabled && evidence.Passed
            ? routes[0].Nominal + (i - 2) * 0.002 : 0).ToArray();
        evidence.Trace(routes[0].Id, "V", samples, 2, DateTimeOffset.UtcNow);
        evidence.Evaluate("voltage", samples.Average(), "V");
        evidence.Evaluate("setpointError", samples.Average() - nominal, "V");
    }
}

[Display("Check Harness Resistance", Groups: ["HardwareTest Examples"])]
public sealed class HarnessResistanceStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("minResistance", "ohm"), ("maxResistance", "ohm"), ("consecutiveDelta", "ohm"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "ohm");
        RequireLevels(row, 2, 2);
        ProcedureTables.Require(row.Levels[0] is >= 2 and <= 100 && row.Levels[0] == Math.Truncate(row.Levels[0])
            && row.Levels[1] > 0, $"Case '{row.Id}' must provide 2–100 samples and a positive interval.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        if (fixture.SupplyEnabled)
            throw new InvalidOperationException("Disable simulated supplies before resistance checks.");
        var route = fixture.ActiveRoutes[0];
        var samples = Enumerable.Range(0, (int)row.Levels[0]).Select(i => route.Nominal + 0.003 * Math.Sin(i)).ToArray();
        evidence.Trace(route.Id, "ohm", samples, row.Levels[1], DateTimeOffset.UtcNow);
        evidence.Evaluate("minResistance", samples.Min(), "ohm");
        evidence.Evaluate("maxResistance", samples.Max(), "ohm");
        evidence.Evaluate("consecutiveDelta", samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).Max(), "ohm");
    }
}

[Display("Query Serial DUT", Groups: ["HardwareTest Examples"])]
public sealed class DutCommunicationsStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("identity", "check"), ("ping", "check"), ("status", "check"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row) => RequireRoutes(tables, row, "serial");

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        if (!fixture.SupplyEnabled)
            throw new InvalidOperationException("Confirm the DUT connection before issuing commands.");
        var identity = fixture.Serial.Query("*IDN?", TapThread.Current.AbortToken);
        var pong = fixture.Serial.Query("PING", TapThread.Current.AbortToken);
        var status = fixture.Serial.Query("STATUS?", TapThread.Current.AbortToken);
        evidence.Evaluate("identity", identity.Split(',').Length == 4 ? 1 : 0, "check");
        evidence.Evaluate("ping", pong == "PONG" ? 1 : 0, "check");
        evidence.Evaluate("status", status == "READY" ? 1 : 0, "check");
        Log.Info("DUT identity {0}, ping {1}, status {2}", identity, pong, status);
    }
}

[Display("Check DUT Data Levels and Clock", Groups: ["HardwareTest Examples"])]
public sealed class DutSignalsStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("low", "V"), ("high", "V"), ("frequency", "Hz"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "V", "V");
        RequireLevels(row, 1, 1);
        ProcedureTables.Require(row.Levels[0] > 0, $"Case '{row.Id}' needs a positive clock interval.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var routes = fixture.ActiveRoutes;
        var waveform = Enumerable.Range(0, 40).Select(i => routes[i % 10 < 5 ? 0 : 1].Nominal).ToArray();
        evidence.Trace("clock", "V", waveform, row.Levels[0], DateTimeOffset.UtcNow);
        evidence.Evaluate("low", waveform.Min(), "V");
        evidence.Evaluate("high", waveform.Max(), "V");
        var rising = Enumerable.Range(1, waveform.Length - 1).Where(i => waveform[i] > waveform[i - 1]).ToArray();
        ProcedureTables.Require(rising.Length >= 2, "DUT clock trace has too few rising edges.");
        evidence.Evaluate("frequency", 1000 / ((rising[1] - rising[0]) * row.Levels[0]), "Hz");
    }
}

internal static class SimulatedDut
{
    public static double ReadNumber(SerialDutPort serial, string command)
        => double.Parse(serial.Query(command, TapThread.Current.AbortToken), NumberStyles.Float, CultureInfo.InvariantCulture);
}
