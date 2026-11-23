using System.Text.Json.Nodes;
using System.Xml.Linq;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;

internal static partial class ProcedureVerification
{
    private static async Task VerifyMeasurementEvidenceAsync(OpenTapWorkerClient worker, string sourcePlan,
        OpenTapRunSummary baseline, AppSettings settings, CancellationToken cancellationToken)
    {
        Check(CapturesFallWithinTheirExecution(baseline), "A capture predates its route selection or exceeds its run.");
        Check(Measurement(baseline, "diff-response.step-1.settlingTime").Value == 5
            && CaptureBeginsAfterSettling(baseline), "Nominal waveform acquisition preceded the settling trigger.");
        Check(Measurement(baseline, "ac-reciprocal.50ohm.step-1.ac-ab.vrms").Value
            != Measurement(baseline, "ac-reciprocal.600ohm.step-1.ac-ab.vrms").Value,
            "The simulated loads produced identical raw VRMS.");
        var generatorA = baseline.Samples.Where(row => row.MetricKey == "pulse-decoder.capture.pulse-gen-a"
            && row.Value > 0).Select(row => row.Timestamp).ToHashSet();
        Check(baseline.Samples.Any(row => row.MetricKey == "pulse-decoder.capture.pulse-gen-b"
            && row.Value > 0 && generatorA.Contains(row.Timestamp)), "The simulated sources did not overlap on the digitizer timebase.");

        var sourceTables = Path.Combine(Path.GetDirectoryName(sourcePlan)!, "tables", "advanced-procedure");
        var stationSource = File.ReadAllText(Path.Combine(sourceTables, "station.json"));
        var testsSource = File.ReadAllText(Path.Combine(sourceTables, "tests.json"));
        var temp = Directory.CreateTempSubdirectory("hardwaretest-measurement-evidence-");
        try
        {
            var targetTables = temp.CreateSubdirectory(Path.Combine("tables", "advanced-procedure")).FullName;
            var plan = Path.Combine(temp.FullName, ProcedurePlan.PlanName);
            var stationPath = Path.Combine(targetTables, "station.json");
            var testPath = Path.Combine(targetTables, "tests.json");
            var station = JsonNode.Parse(stationSource)!;
            var tests = JsonNode.Parse(testsSource)!;
            var leak = Row(station, "routes", "logic-data-leak");
            leak["nominal"] = 0.5;
            File.WriteAllText(stationPath, station.ToJsonString());
            File.WriteAllText(testPath, testsSource);
            File.Copy(sourcePlan, plan);
            await worker.LoadPlanAsync(plan, cancellationToken);
            var bridge = await worker.RunAsync(cancellationToken: cancellationToken);
            Check(bridge.Result == RunResult.Failed && Measurement(bridge, "rail-logic.crossRailLeak").RowVerdict == "Fail"
                && Measurement(bridge, "rail-logic.groundLeak").RowVerdict == "Pass" && HasFixtureReset(bridge)
                && bridge.Events.Count(mark => mark.Name == "case.route") == 1, "A logic-to-data short bypassed the rail gate.");
            await SaveReportsAsync(bridge, settings, cancellationToken);
            Console.WriteLine("A logic-to-data short independently failed the station gate.");

            leak["nominal"] = 0.01;
            var calibrations = station["calibrations"]!.AsArray();
            var removed = calibrations[0]!.DeepClone();
            calibrations.RemoveAt(0);
            File.WriteAllText(stationPath, station.ToJsonString());
            var missing = await worker.RunAsync(cancellationToken: cancellationToken);
            Check(missing.Result == RunResult.Error && missing.Samples.Count == 0
                && missing.ErrorMessage?.Contains("No calibration", StringComparison.Ordinal) == true,
                "A missing station calibration did not block acquisition.");
            calibrations.Insert(0, removed);
            Console.WriteLine("Missing calibration blocked measurement during shared preflight.");

            Row(station, "routes", "hw-counter")["counterReadings"] = new JsonArray(1000, 1008, 1021, 1036);
            var reference = Row(tests, "references", "profile-nominal-r3")["values"]!.AsArray();
            reference[^1] = reference[^1]!.GetValue<double>() + 0.09;
            Row(tests, "cases", "clamp-load")["levels"]![0] = 0.1;
            File.WriteAllText(stationPath, station.ToJsonString());
            File.WriteAllText(testPath, tests.ToJsonString());
            SetMeasurementSetting(plan, "diff-response", "ResponseTimeConstantMs", 1.25);
            SetMeasurementSetting(plan, "pulse-decoder", "FirstRiseSamples", 3);
            await worker.LoadPlanAsync(plan, cancellationToken);
            var choices = new[] { "Crossed", "Terminated", "Loopback" };
            var mixed = await RespondAsync(worker, choices, false, cancellationToken);

            Check(mixed.Result == RunResult.Failed && CapturesFallWithinTheirExecution(mixed)
                && CaptureBeginsAfterSettling(mixed)
                && Measurement(mixed, "diff-response.step-1.settlingTime").Value == 6,
                "The slower simulated response did not move the settling trigger.");
            Check(mixed.Samples.Where(row => row.MetricKey.StartsWith("diff-response.", StringComparison.Ordinal)
                    && row.RowVerdict == "Fail").Select(row => row.MetricKey).SequenceEqual(["diff-response.step-5.previousDelta"]),
                "A nonuniform reference perturbation did not isolate the adjacent error.");
            Check(Measurement(mixed, "clamp-load.normal.clampTime") is { Value: 0, RowVerdict: "Fail" }
                && Measurement(mixed, "clamp-load.normal.reclampTime") is { Value: 0, RowVerdict: "Fail" }
                && Measurement(mixed, "clamp-load.normal.pulseWidth") is { Value: 10, RowVerdict: "Pass" },
                "A transient below the sample resolution did not produce measurable limit failures.");
            Check(Measurement(mixed, "counter-relative.trigger-2.increment").Value == 1
                && Measurement(mixed, "counter-relative.trigger-3.increment").Value == -1
                && Measurement(mixed, "counter-relative.span").Value == 36
                && Measurement(mixed, "counter-relative.dutDelta").RowVerdict == "Pass",
                "Counter readings failed to distinguish observed increments from stimulus and final span.");
            Check(Measurement(mixed, "pulse-decoder.edgeShape").RowVerdict == "Fail"
                && Measurement(mixed, "pulse-decoder.payload").RowVerdict == "Pass"
                && Measurement(mixed, "pulse-decoder.decodedWord").Value == 45
                && Measurement(mixed, "pulse-decoder.windowCompliance").RowVerdict == "Pass",
                "A single malformed edge did not fail the waveform independently of payload.");
            await SaveReportsAsync(mixed, settings, cancellationToken);
            Console.WriteLine("Delayed trigger, isolated adjacent error, unresolved clamp transients, counter discrepancies, and a malformed edge verified.");

            station = JsonNode.Parse(stationSource)!;
            Row(station, "routes", "pulse-gen-b")["nominal"] = 0;
            File.WriteAllText(stationPath, station.ToJsonString());
            File.WriteAllText(testPath, testsSource);
            File.Copy(sourcePlan, plan, overwrite: true);
            await worker.LoadPlanAsync(plan, cancellationToken);
            var wrongWord = await RespondAsync(worker, choices, false, cancellationToken);
            Check(wrongWord.Result == RunResult.Failed && Measurement(wrongWord, "pulse-decoder.expectedWord").Value == 45
                && Measurement(wrongWord, "pulse-decoder.decodedWord").Value == 0
                && wrongWord.Samples.Where(row => row.RowVerdict == "Fail")
                    .Select(row => row.MetricKey).SequenceEqual(["pulse-decoder.payload"]),
                "The wrong decoded word did not fail independently of waveform compliance.");
            await SaveReportsAsync(wrongWord, settings, cancellationToken);
            Console.WriteLine("Compliant waveform with a wrong decoded word verified.");
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private static StoredSample Measurement(OpenTapRunSummary run, string metricKey)
        => run.Samples.Single(row => row.MetricKey == metricKey);

    private static bool CapturesFallWithinTheirExecution(OpenTapRunSummary run)
    {
        var routes = run.Events.Where(mark => mark.Name == "case.route" && mark.StepRunId.HasValue)
            .ToDictionary(mark => mark.StepRunId!.Value, mark => mark.Timestamp);
        return run.Samples.Where(row => row.SampleIndex.HasValue)
            .All(row => row.StepRunId is { } id && routes.TryGetValue(id, out var selected)
                && row.Timestamp >= selected && row.Timestamp >= run.StartedAt && row.Timestamp <= run.CompletedAt);
    }

    private static bool CaptureBeginsAfterSettling(OpenTapRunSummary run)
    {
        const string root = "diff-response.capture.step-1.";
        var tracking = run.Samples.Where(row => row.MetricKey == root + "tracking").Max(row => row.Timestamp);
        var channels = run.Samples.Where(row => row.MetricKey is var key
            && key.StartsWith(root, StringComparison.Ordinal) && key != root + "tracking")
            .GroupBy(row => row.MetricKey).ToArray();
        var firstSamples = channels.Select(channel => channel.Min(row => row.Timestamp)).Distinct().ToArray();
        return channels.Length == 3 && firstSamples.Length == 1 && firstSamples[0] > tracking;
    }

    private static void SetMeasurementSetting(string plan, string id, string name, object value)
    {
        var document = XDocument.Load(plan);
        var step = document.Descendants().Single(element => element.Name.LocalName == "TestStep"
            && (string?)element.Element(element.Name.Namespace + "CaseId") == id);
        step.SetElementValue(step.Name.Namespace + name, value);
        document.Save(plan);
    }
}
