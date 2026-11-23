using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Profile Differential Output", Groups: ["HardwareTest Examples"])]
public sealed class DifferentialProfileStep : FixtureMeasurementStep
{
    public int CaptureSamples { get; set; } = 96;

    internal override (string Id, string Unit)[] Criteria =>
    [
        ("settlingTime", "ms"), ("unloadedGain", "ratio"), ("loadedStrength", "ratio"), ("residual", "dBc"),
        ("referenceDelta", "V"), ("previousDelta", "V"), ("spanDelta", "V"), ("curveScore", "pts"),
    ];

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        ProcedureTables.Require(CaptureSamples is >= 24 and <= 4096, $"Case '{row.Id}' needs 24–4096 samples per capture channel.");
        RequireRoutes(tables, row, "V", "V", "V");
        RequireLevels(row, 3, 20);
        ProcedureTables.Require(tables.Reference(row).Length == row.Levels.Length && row.Levels.All(level => level > 0)
            && row.Levels.SequenceEqual(row.Levels.Distinct().Order()), $"Case '{row.Id}' needs an increasing positive ladder and a matching reference.");
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
            var tracking = Enumerable.Range(0, 24).Select(n => target * (1 - Math.Exp(-n / 2.0))).ToArray();
            var settled = Array.FindIndex(tracking, 3, sample => Math.Abs(target - sample) <= target * 0.01);
            ProcedureTables.Require(settled > 0, $"No settling trigger at {coordinate}.");
            var capturedAt = DateTimeOffset.UtcNow;
            evidence.Trace($"{coordinate}.tracking", "V", tracking[..(settled + 1)], 0.5, capturedAt);
            evidence.Evaluate("settlingTime", settled * 0.5, "ms", coordinate);

            var nSamples = CaptureSamples;
            var positive = Waveforms.Sine(target / 2, nSamples, 3);
            var negative = Waveforms.Sine(-level * routes[1].Nominal / 2, nSamples, 3);
            var differential = positive.Zip(negative, (p, n) => p - n).ToArray();
            var loaded = differential.Select((sample, n) => sample * routes[2].Nominal
                + level * 0.002 * Math.Sin(2 * Math.PI * 6 * n / nSamples)).ToArray();
            evidence.Trace($"{coordinate}.positive", "V", positive, 0.05, capturedAt);
            evidence.Trace($"{coordinate}.negative", "V", negative, 0.05, capturedAt);
            evidence.Trace($"{coordinate}.loaded", "V", loaded, 0.05, capturedAt);

            var unloaded = Waveforms.Rms(differential);
            var loadedRms = Waveforms.Rms(loaded);
            loadedValues[i] = loadedRms;
            errors[i] = reference[i] - loadedRms;
            var fundamental = Waveforms.SpectralMagnitude(loaded, 3);
            var harmonic = Waveforms.SpectralMagnitude(loaded, 6);
            evidence.Evaluate("unloadedGain", unloaded / level, "ratio", coordinate);
            evidence.Evaluate("loadedStrength", loadedRms / unloaded, "ratio", coordinate);
            evidence.Evaluate("residual", 20 * Math.Log10(harmonic / fundamental), "dBc", coordinate);
            evidence.Evaluate("referenceDelta", errors[i], "V", coordinate);
            if (i > 0)
            {
                var observedChange = loadedValues[i - 1] - loadedRms;
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

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        RequireRoutes(tables, row, "A");
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
            var data = Enumerable.Range(0, 64).Select(i =>
            {
                var t = i * dt;
                if (t < 2 || t >= 12)
                    return 0.05;
                if (t < 2 + delayMs || (t >= 7 && t < 7 + delayMs))
                    return nominal + 0.22;
                return nominal;
            }).ToArray();
            evidence.Trace(coordinate, "A", data, dt, DateTimeOffset.UtcNow);
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
            evidence.Evaluate("sensorDelta", sensor - data[reclamp..end].Average(), "A", coordinate);
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

    internal override void Validate(ProcedureTables tables, CaseTable row)
    {
        var routes = RequireRoutes(tables, row, "VRMS", "VRMS");
        RequireLevels(row, 5, 20);
        ProcedureTables.Require(row.Levels.Take(2).All(value => value > 0) && row.Levels[0] != row.Levels[1]
            && row.Levels.Skip(2).SequenceEqual(row.Levels.Skip(2).Distinct().Order()),
            $"Case '{row.Id}' needs two distinct positive impedances followed by an increasing attenuation ladder.");
        foreach (var impedance in row.Levels.Take(2))
            foreach (var route in routes)
                _ = tables.CorrectionDb(route, impedance);
    }

    internal override void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence)
    {
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
                var measured = new double[routes.Length];
                var captured = DateTimeOffset.UtcNow;
                for (var path = 0; path < routes.Length; path++)
                {
                    var route = routes[path];
                    var measuredVrms = route.Nominal * Math.Pow(10, -target / 20);
                    var signal = Waveforms.Sine(measuredVrms * Math.Sqrt(2), 64, 4, path * 0.12);
                    evidence.Trace($"{coord}.{route.Id}", "V", signal, 0.025, captured);
                    var vrms = Waveforms.Rms(signal);
                    measured[path] = -20 * Math.Log10(vrms) + fixture.Tables.CorrectionDb(route, impedance);
                    evidence.Evaluate("vrms", vrms, "VRMS", $"{coord}.{route.Id}");
                    evidence.Evaluate("referenceDelta", target - measured[path], "dB", $"{coord}.{route.Id}");
                }

                evidence.Evaluate("equalization", measured[0] - measured[1], "dB", coord);
                var current = measured.Average();
                if (i == 0)
                    first = current;
                else
                    evidence.Evaluate("previousDelta", (ladder[i - 1] - target) - (previous - current), "dB", coord);
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
