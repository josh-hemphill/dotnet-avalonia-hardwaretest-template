using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Evaluate Accumulated Counter Triggers", Groups: ["HardwareTest Examples"])]
public sealed class CounterTriggerStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("increment", "count"), ("span", "count"), ("dutDelta", "count"),
    ];

    internal override (string Id, string Unit)[] OptionalCriteria => [("observedIncrement", "count")];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        var route = RequireRoutes(tables, row, "count")[0];
        RequireLevels(row, 3, 20);
        ProcedureTables.Require(row.Levels.All(level => level > 0 && level == Math.Truncate(level)),
            $"Case '{row.Id}' requires positive integer trigger increments.");
        if (route.CounterReadings is { } readings)
            ProcedureTables.Require(readings.Length == row.Levels.Length + 1
                && readings.All(value => double.IsFinite(value) && value >= 0 && value == Math.Truncate(value))
                && readings.SequenceEqual(readings.Order()),
                $"Case '{row.Id}' needs a recorded baseline and one nondecreasing integer reading per trigger.");
        else
            ProcedureTables.Require(!row.Criteria.Any(band => band.Id == "observedIncrement"),
                $"Case '{row.Id}' needs recorded counter readings to evaluate observed increments.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var route = fixture.ActiveRoutes[0];
        var counts = route.CounterReadings ?? SimulateIdealCounter(route.Nominal, row.Levels);
        evidence.Trace("accumulated", "count", counts, 1, DateTimeOffset.UtcNow);
        for (var i = 0; i < row.Levels.Length; i++)
        {
            var observed = counts[i + 1] - counts[i];
            var coordinate = $"trigger-{i + 1}";
            evidence.Evaluate("increment", observed - row.Levels[i], "count", coordinate);
            evidence.EvaluateOptional("observedIncrement", observed, "count", coordinate);
        }

        evidence.Evaluate("span", counts[^1] - counts[0], "count");
        evidence.Evaluate("dutDelta", SimulatedDut.ReadNumber(fixture.Serial, "COUNTER?") - counts[^1], "count");
    }

    // Tables written before recorded counter inputs can still reproduce their ideal simulation.
    private static double[] SimulateIdealCounter(double baseline, double[] increments)
    {
        var counts = new double[increments.Length + 1];
        counts[0] = baseline;
        for (var i = 0; i < increments.Length; i++)
            counts[i + 1] = counts[i] + increments[i];
        return counts;
    }
}

[Display("Decode Pulse-Width Data", Groups: ["HardwareTest Examples"])]
public sealed class DecodePulseWidthStep : FixtureMeasurementStep
{
    private const double ThresholdVolts = 1.5;

    public int FirstRiseSamples { get; set; } = 1;

    internal override (string Id, string Unit)[] Criteria =>
    [
        ("payload", "check"), ("peakToPeak", "V"), ("highMinimum", "V"), ("lowMaximum", "V"),
        ("edgeShape", "V/sample"), ("width", "ms"), ("period", "ms"), ("frequency", "Hz"),
        ("windowCompliance", "check"),
    ];

    internal override (string Id, string Unit)[] OptionalCriteria =>
        [("decodedWord", "count"), ("expectedWord", "count")];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "V", "V", "V");
        RequireLevels(row, 4, 8);
        ProcedureTables.Require(FirstRiseSamples is >= 1 and <= 3,
            $"Case '{row.Id}' needs one to three samples in the simulated first rising edge.");
        ProcedureTables.Require(row.Levels.All(bit => bit is 0 or 1), $"Case '{row.Id}' expects a binary payload.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var routes = fixture.ActiveRoutes;
        const double dt = 0.1;
        const int window = 20;
        var samples = row.Levels.Length * window + 5;
        var generatorA = new double[samples];
        var generatorB = new double[samples];
        var signal = new double[samples];
        for (var i = 0; i < samples; i++)
        {
            var bit = (i - 3) / window;
            var phase = (i - 3) % window;
            var active = i >= 3 && bit < row.Levels.Length;
            generatorA[i] = active && phase < 5 ? routes[0].Nominal : 0;
            generatorB[i] = active && row.Levels[bit] == 1 && phase is >= 4 and < 12 ? routes[1].Nominal : 0;
            var high = generatorA[i] + generatorB[i] > 0;
            const double low = 0.08;
            var transition = bit == 0 && high ? Math.Min(1.0, (phase + 1) / (double)FirstRiseSamples) : 1.0;
            var voltage = high ? low + (routes[2].Nominal - low) * transition : low;
            signal[i] = voltage + 0.02 * Math.Sin(i);
        }

        var capturedAt = DateTimeOffset.UtcNow;
        evidence.Trace(routes[0].Id, "V", generatorA, dt, capturedAt);
        evidence.Trace(routes[1].Id, "V", generatorB, dt, capturedAt);
        evidence.Trace(routes[2].Id, "V", signal, dt, capturedAt);
        var pulses = Decode(signal, dt, out var complete);
        var expected = row.Levels.Aggregate(0, (word, bit) => (word << 1) | (int)bit);
        var decoded = pulses.Aggregate(0, (word, pulse) => (word << 1) | (pulse.Width >= 0.8 ? 1 : 0));
        evidence.Evaluate("payload", decoded == expected && pulses.Count == row.Levels.Length && complete ? 1 : 0, "check");
        evidence.EvaluateOptional("decodedWord", decoded, "count");
        evidence.EvaluateOptional("expectedWord", expected, "count");
        evidence.Evaluate("peakToPeak", signal.Max() - signal.Min(), "V");
        evidence.Evaluate("highMinimum", signal.Where(value => value >= ThresholdVolts).DefaultIfEmpty(0).Min(), "V");
        evidence.Evaluate("lowMaximum", signal.Where(value => value < ThresholdVolts).DefaultIfEmpty(3.3).Max(), "V");
        var edgeChanges = signal.Zip(signal.Skip(1))
            .Where(edge => (edge.First >= ThresholdVolts) != (edge.Second >= ThresholdVolts))
            .Select(edge => Math.Abs(edge.First - edge.Second));
        evidence.Evaluate("edgeShape", edgeChanges.DefaultIfEmpty(0).Min(), "V/sample");

        var compliant = complete && pulses.Count == row.Levels.Length;
        for (var i = 0; i < pulses.Count; i++)
        {
            var pulse = pulses[i];
            var coordinate = $"pulse-{i + 1}";
            var width = pulse.Width;
            var center = width >= 0.8 ? 1.2 : 0.5;
            compliant &= Math.Abs(width - center) <= 0.15;
            evidence.Evaluate("width", width, "ms", coordinate);
            if (i > 0)
            {
                var period = pulse.Start - pulses[i - 1].Start;
                compliant &= Math.Abs(period - window * dt) <= 0.15;
                evidence.Evaluate("period", period, "ms", coordinate);
                evidence.Evaluate("frequency", 1000 / period, "Hz", coordinate);
            }
        }

        if (pulses.Count < 2)
        {
            // A missing edge is measurable evidence, including when the decoder returns nothing.
            if (pulses.Count == 0)
                evidence.Evaluate("width", 0, "ms");
            evidence.Evaluate("period", 0, "ms");
            evidence.Evaluate("frequency", 0, "Hz");
        }

        evidence.Evaluate("windowCompliance", compliant ? 1 : 0, "check");
    }

    private static List<(double Start, double Width)> Decode(double[] trace, double intervalMs, out bool complete)
    {
        var pulses = new List<(double Start, double Width)>();
        int? start = null;
        for (var i = 0; i < trace.Length; i++)
        {
            if (trace[i] >= ThresholdVolts)
                start ??= i;
            else if (start is { } first)
            {
                pulses.Add((first * intervalMs, (i - first) * intervalMs));
                start = null;
            }
        }

        complete = start is null && trace[0] < ThresholdVolts;
        return pulses;
    }
}

[Display("Evaluate Fixture Configuration", Groups: ["HardwareTest Examples"])]
public sealed class ConfigurationMeasurementStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("output", "V"), ("referenceDelta", "V"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "V");
        RequireLevels(row, 1, 1);
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        if (!fixture.SupplyEnabled)
            throw new InvalidOperationException("Confirm the fixture configuration before measuring it.");
        // Model the output settling after configuration before taking a voltage reading.
        TapThread.Sleep(250);
        WaitIfPaused();
        TapThread.ThrowIfAborted();
        var voltage = fixture.ActiveRoutes[0].Nominal;
        evidence.Evaluate("output", voltage, "V");
        evidence.Evaluate("referenceDelta", row.Levels[0] - voltage, "V");
    }
}
