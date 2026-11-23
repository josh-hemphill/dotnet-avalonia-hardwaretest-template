using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Profile Differential Output", Groups: ["HardwareTest Examples"])]
public sealed class DifferentialProfileStep : FixtureMeasurementStep
{
    private const double TrackingIntervalMs = 0.5;
    private const double WaveformIntervalMs = 0.05;
    private const int TrackingSamples = 24;

    public int CaptureSamples { get; set; } = 96;
    public double ResponseTimeConstantMs { get; set; } = 1;

    internal override (string Id, string Unit)[] Criteria =>
    [
        ("settlingTime", "ms"), ("unloadedGain", "ratio"), ("loadedStrength", "ratio"), ("residual", "dBc"),
        ("referenceDelta", "V"), ("previousDelta", "V"), ("spanDelta", "V"), ("curveScore", "pts"),
    ];

    internal override (string Id, string Unit)[] OptionalCriteria =>
    [
        ("driveVoltage", "V"), ("settlingRate", "V/ms"), ("unloadedVrms", "VRMS"), ("loadedVrms", "VRMS"),
        ("previousChange", "V"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        ProcedureTables.Require(double.IsFinite(ResponseTimeConstantMs) && ResponseTimeConstantMs > 0,
            $"Case '{row.Id}' needs a positive response time constant.");
        ProcedureTables.Require(CaptureSamples is >= 24 and <= 4096, $"Case '{row.Id}' needs 24–4096 samples per capture channel.");
        var routes = tables.Routes(row);
        RequireRoutes(tables, row, "V", "V", routes.LastOrDefault()?.Unit == "ratio" ? "ratio" : "V");
        ProcedureTables.Require(routes.All(route => route.Nominal > 0), $"Case '{row.Id}' needs positive differential amplitudes and load scaling.");
        RequireLevels(row, 3, 20);
        var reference = tables.Reference(row);
        ProcedureTables.Require(reference.Length == row.Levels.Length && reference.Any(value => value != 0)
            && row.Levels.All(level => level > 0) && row.Levels.SequenceEqual(row.Levels.Distinct().Order()),
            $"Case '{row.Id}' needs an increasing positive ladder and a matching nonzero reference.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var routes = fixture.ActiveRoutes;
        var reference = fixture.Tables.Reference(row);
        var loadedValues = new double[row.Levels.Length];
        var errors = new double[row.Levels.Length];
        for (var i = 0; i < row.Levels.Length; i++)
        {
            WaitIfPaused();
            TapThread.ThrowIfAborted();
            var level = row.Levels[i];
            var coordinate = $"step-{i + 1}";
            var target = level * routes[0].Nominal;
            var tracking = Enumerable.Range(0, TrackingSamples)
                .Select(n => target * (1 - Math.Exp(-n * TrackingIntervalMs / ResponseTimeConstantMs))).ToArray();
            var settled = Array.FindIndex(tracking, 3, sample => Math.Abs(target - sample) <= target * 0.01);
            ProcedureTables.Require(settled > 0, $"No settling trigger at {coordinate}.");
            var settlingTime = settled * TrackingIntervalMs;
            var nSamples = CaptureSamples;
            var waveformDurationMs = nSamples * WaveformIntervalMs;
            var capturedAt = CompleteSimulatedCapture(settlingTime + waveformDurationMs);
            var trigger = capturedAt.AddMilliseconds(-waveformDurationMs);
            evidence.Trace($"{coordinate}.tracking", "V", tracking[..(settled + 1)], TrackingIntervalMs, trigger);
            evidence.Evaluate("settlingTime", settlingTime, "ms", coordinate);
            evidence.EvaluateOptional("settlingRate", (tracking[settled] - tracking[0]) / settlingTime, "V/ms", coordinate);
            evidence.EvaluateOptional("driveVoltage", target, "V", coordinate);

            var positive = Waveforms.Sine(target / 2, nSamples, 3);
            var negative = Waveforms.Sine(-level * routes[1].Nominal / 2, nSamples, 3);
            var differential = positive.Zip(negative, (p, n) => p - n).ToArray();
            var loaded = differential.Select((sample, n) => sample * routes[2].Nominal
                + level * 0.002 * Math.Sin(2 * Math.PI * 6 * n / nSamples)).ToArray();
            evidence.Trace($"{coordinate}.positive", "V", positive, WaveformIntervalMs, capturedAt);
            evidence.Trace($"{coordinate}.negative", "V", negative, WaveformIntervalMs, capturedAt);
            evidence.Trace($"{coordinate}.loaded", "V", loaded, WaveformIntervalMs, capturedAt);

            var unloaded = Waveforms.Rms(differential);
            var loadedRms = Waveforms.Rms(loaded);
            loadedValues[i] = loadedRms;
            errors[i] = reference[i] - loadedRms;
            var fundamental = Waveforms.SpectralMagnitude(loaded, 3);
            var harmonic = Waveforms.SpectralMagnitude(loaded, 6);
            evidence.Evaluate("unloadedGain", unloaded / target, "ratio", coordinate);
            evidence.Evaluate("loadedStrength", loadedRms / unloaded, "ratio", coordinate);
            evidence.EvaluateOptional("unloadedVrms", unloaded, "VRMS", coordinate);
            evidence.EvaluateOptional("loadedVrms", loadedRms, "VRMS", coordinate);
            evidence.Evaluate("residual", 20 * Math.Log10(harmonic / fundamental), "dBc", coordinate);
            evidence.Evaluate("referenceDelta", errors[i], "V", coordinate);
            if (i > 0)
            {
                var observedChange = loadedValues[i - 1] - loadedRms;
                evidence.EvaluateOptional("previousChange", observedChange, "V", coordinate);
                var referenceChange = reference[i - 1] - reference[i];
                evidence.Evaluate("previousDelta", observedChange - referenceChange, "V", coordinate);
            }
        }

        evidence.Evaluate("spanDelta", (reference[^1] - reference[0]) - (loadedValues[^1] - loadedValues[0]), "V");
        evidence.Evaluate("curveScore", Math.Clamp(100 * (1 - Waveforms.Rms(errors) / Waveforms.Rms(reference)), 0, 100), "pts");
    }
}

[Display("Compare Load Clamp and DUT Sensor", Groups: ["HardwareTest Examples"])]
public sealed class ClampAndSensorStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("clampTime", "ms"), ("reclampTime", "ms"), ("pulseWidth", "ms"), ("peakCurrent", "A"),
        ("sensorDelta", "A"),
    ];

    internal override (string Id, string Unit)[] OptionalCriteria =>
        [("responseDelay", "ms"), ("externalCurrent", "A"), ("dutCurrent", "A")];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        var routes = RequireRoutes(tables, row, "A");
        ProcedureTables.Require(routes[0].Nominal > 0.1, $"Case '{row.Id}' needs a clamp target above the simulated off current.");
        RequireLevels(row, 2, 2);
        ProcedureTables.Require(row.Levels.All(level => level > 0) && row.Levels[0] <= 2 && row.Levels[1] <= 2,
            $"Case '{row.Id}' needs clamp delays in the range (0, 2] ms.");
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        var nominal = fixture.ActiveRoutes[0].Nominal;
        const double dt = 0.25;
        foreach (var (delayMs, coordinate) in row.Levels.Zip(new[] { "normal", "delayed" }))
        {
            const double commandStart = 1.5;
            var responseStart = commandStart + delayMs;
            var data = Enumerable.Range(0, 64).Select(i =>
            {
                var t = i * dt;
                if (t < responseStart || t >= responseStart + 10)
                    return 0.05;
                if (t < responseStart + delayMs || (t >= responseStart + 5 && t < responseStart + 5 + delayMs))
                    return nominal + 0.22;
                return nominal;
            }).ToArray();
            var command = Enumerable.Range(0, data.Length)
                .Select(i => i * dt >= commandStart && i * dt < commandStart + 10 ? 1.0 : 0).ToArray();
            var capturedAt = CompleteSimulatedCapture((data.Length - 1) * dt);
            evidence.Trace(coordinate, "A", data, dt, capturedAt);
            evidence.Trace($"{coordinate}.loadCommand", "check", command, dt, capturedAt);
            var onset = Array.FindIndex(data, sample => sample > nominal + 0.1);
            var firstClamp = Array.FindIndex(data, onset, sample => sample <= nominal);
            var disturbance = Array.FindIndex(data, firstClamp, sample => sample > nominal + 0.1);
            var reclamp = Array.FindIndex(data, disturbance, sample => sample <= nominal);
            var end = Array.FindIndex(data, reclamp, sample => sample < nominal / 2);
            ProcedureTables.Require(end >= 0, $"Case '{row.Id}' did not capture the complete load pulse.");
            var sensor = SimulatedDut.ReadNumber(fixture.Serial, "SENSOR:CURRENT?");
            evidence.Evaluate("clampTime", (firstClamp - onset) * dt, "ms", coordinate);
            evidence.Evaluate("reclampTime", (reclamp - disturbance) * dt, "ms", coordinate);
            evidence.Evaluate("pulseWidth", (end - onset) * dt, "ms", coordinate);
            evidence.Evaluate("peakCurrent", data.Max(), "A", coordinate);
            var external = data[reclamp..end].Average();
            evidence.Evaluate("sensorDelta", sensor - external, "A", coordinate);
            evidence.EvaluateOptional("responseDelay", onset * dt - commandStart, "ms", coordinate);
            evidence.EvaluateOptional("externalCurrent", external, "A", coordinate);
            evidence.EvaluateOptional("dutCurrent", sensor, "A", coordinate);
        }
    }
}

[Display("Compare Calibrated Reciprocal AC Paths", Groups: ["HardwareTest Examples"])]
public sealed class ReciprocalAcStep : FixtureMeasurementStep
{
    internal override (string Id, string Unit)[] Criteria =>
    [
        ("vrms", "VRMS"), ("referenceDelta", "dB"), ("equalization", "dB"), ("previousDelta", "dB"),
        ("spanDelta", "dB"),
    ];

    internal override (string Id, string Unit)[] OptionalCriteria => [("previousChange", "dB")];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        var routes = RequireRoutes(tables, row, "VRMS", "VRMS");
        ProcedureTables.Require(routes.All(route => route.Nominal > 0),
            $"Case '{row.Id}' needs positive AC source levels.");
        RequireLevels(row, 5, 20);
        ProcedureTables.Require(row.Levels.Take(2).All(value => value > 0) && row.Levels[0] != row.Levels[1]
            && row.Levels.Skip(2).SequenceEqual(row.Levels.Skip(2).Distinct().Order()),
            $"Case '{row.Id}' needs two distinct positive impedances followed by an increasing attenuation ladder.");
        foreach (var impedance in row.Levels.Take(2))
            foreach (var route in routes)
                _ = tables.CalibrationFor(route, impedance);
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
        const double referenceVrms = 1;
        var routes = fixture.ActiveRoutes;
        foreach (var impedance in row.Levels.Take(2))
        {
            var ladder = row.Levels.Skip(2).ToArray();
            var first = 0.0;
            var previous = 0.0;
            var impedanceId = FormattableString.Invariant($"{impedance:G}ohm");
            for (var i = 0; i < ladder.Length; i++)
            {
                WaitIfPaused();
                TapThread.ThrowIfAborted();
                var target = ladder[i];
                var coord = $"{impedanceId}.step-{i + 1}";
                const int sampleCount = 64;
                const double intervalMs = 0.025;
                var measured = new double[routes.Length];
                var captured = CompleteSimulatedCapture((sampleCount - 1) * intervalMs);
                for (var path = 0; path < routes.Length; path++)
                {
                    var route = routes[path];
                    var calibration = fixture.Tables.CalibrationFor(route, impedance);
                    var measuredVrms = route.Nominal * (calibration.SimulatedVoltageScale ?? 1) * Math.Pow(10, -target / 20);
                    var signal = Waveforms.Sine(measuredVrms * Math.Sqrt(2), sampleCount, 4, path * 0.12);
                    evidence.Trace($"{coord}.{route.Id}", "V", signal, intervalMs, captured);
                    var vrms = Waveforms.Rms(signal);
                    measured[path] = -20 * Math.Log10(vrms / referenceVrms) + calibration.CorrectionDb;
                    evidence.Evaluate("vrms", vrms, "VRMS", $"{coord}.{route.Id}");
                    evidence.Evaluate("referenceDelta", target - measured[path], "dB", $"{coord}.{route.Id}");
                }

                evidence.Evaluate("equalization", measured[0] - measured[1], "dB", coord);
                var current = measured.Average();
                if (i == 0)
                    first = current;
                else
                {
                    var observedChange = previous - current;
                    evidence.EvaluateOptional("previousChange", observedChange, "dB", coord);
                    evidence.Evaluate("previousDelta", (ladder[i - 1] - target) - observedChange, "dB", coord);
                }
                if (i == ladder.Length - 1)
                    evidence.Evaluate("spanDelta", (target - ladder[0]) - (current - first), "dB", impedanceId);
                previous = current;
            }
        }
    }
}

internal static class Waveforms
{
    public static double[] Sine(double amplitude, int samples, int cycles, double phase = 0)
        => [.. Enumerable.Range(0, samples).Select(n => amplitude * Math.Sin(2 * Math.PI * cycles * n / samples + phase))];

    public static double Rms(double[] samples) => Math.Sqrt(samples.Average(value => value * value));

    public static double SpectralMagnitude(double[] samples, int bin)
    {
        var real = 0.0;
        var imaginary = 0.0;
        for (var n = 0; n < samples.Length; n++)
        {
            var phase = 2 * Math.PI * bin * n / samples.Length;
            real += samples[n] * Math.Cos(phase);
            imaginary -= samples[n] * Math.Sin(phase);
        }

        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / samples.Length;
    }
}
