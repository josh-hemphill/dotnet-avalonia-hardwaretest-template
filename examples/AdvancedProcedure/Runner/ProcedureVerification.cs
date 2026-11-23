using System.Text.Json.Nodes;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Host.Worker;
using HardwareTest.OpenTap.Plugins.Basic;

internal static class ProcedureVerification
{
    public static async Task RunAsync(string appDirectory, string dataDirectory)
    {
        var programPath = Path.GetFullPath(Path.Combine(appDirectory, "Programs", ProcedurePlan.PlanName));
        var workerPath = Path.GetFullPath(Path.Combine(appDirectory, OperatingSystem.IsWindows()
            ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker"));
        dataDirectory = Path.GetFullPath(dataDirectory);
        var settings = new AppSettings
        {
            DataDirectory = dataDirectory,
            OpenTapPluginDirectories = [Path.Combine(dataDirectory, "plugins", "AdvancedProcedure")],
            ExportOpenTapResults = true,
            UseMockVisa = true,
            CrashEnabled = false,
        };
        using var worker = new OpenTapWorkerClient(settings, executablePath: workerPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await worker.LoadPlanAsync(programPath, timeout.Token);

        Check(worker.InstrumentSlots.Count == 2 && worker.InstrumentSlots.Count(slot => slot.ResourceName == "MOCK::SERIAL") == 1,
            "Expected a single serial resource and a single identity meter.");
        await worker.ApplyStationAndDutAsync(new StationProfile(new Dictionary<string, string>()),
            new DutIdentity("EXAMPLE-01", null, null, "demo"), timeout.Token);

        var selector = worker.StepTree.SelectMany(Flatten).Single(step => step.Name == "Decode dual-generator digitizer capture");
        var rejected = await worker.RunSelectionAsync(selector.Path, cancellationToken: timeout.Token);
        Check(rejected.Result == RunResult.Error && rejected.Samples.Count == 0
            && rejected.ErrorMessage?.Contains("Run the full procedure", StringComparison.Ordinal) == true,
            "A selected suite bypassed prerequisites or failed to explain how to run it.");

        var cancelled = await RespondAsync(worker, [], cancelAtConfiguration: true, timeout.Token);
        Check(cancelled.Result == RunResult.Cancelled, $"Unexpected cancel result {cancelled.Result}.");
        Check(HasFixtureReset(cancelled), "Cancellation omitted fixture reset evidence.");
        Check(!cancelled.Events.Any(mark => mark.Name.StartsWith("fixture.configuration", StringComparison.Ordinal)),
            "A cancelled prompt started a configuration.");

        var interrupted = await RespondAsync(worker, ["Crossed"], cancelAtConfiguration: false, timeout.Token,
            abortAfterConfigurationStarts: true);
        Check(interrupted.Result == RunResult.Cancelled && HasFixtureReset(interrupted)
            && interrupted.Events.Any(mark => mark.Name == "fixture.configuration.started" && mark.Label == "Crossed")
            && !interrupted.Events.Any(mark => mark.Name == "fixture.configuration"),
            "Aborting a started configuration did not reset the fixture or recorded a completion.");
        Console.WriteLine("Cancelled at the prompt and after Crossed started. Both runs reset with no configuration completion.");

        var choices = new[] { "invalid", "Crossed", "Crossed", "Terminated", "Loopback" };
        var full = await RespondAsync(worker, choices, cancelAtConfiguration: false, timeout.Token);
        Check(full.Result == RunResult.Passed, $"Full procedure returned {full.Result}. {full.ErrorMessage}\n"
            + string.Join("\n", full.Samples.Where(sample => sample.RowVerdict == "Fail").Select(sample => sample.ToDisplayLine())));
        var events = full.Events.Where(mark => mark.Name == "fixture.configuration").Select(mark => mark.Label).ToArray();
        Check(events.SequenceEqual(new[] { "Crossed", "Terminated", "Loopback" }), "Configuration order differs from the operator's choices.");
        Check(full.Events.Where(mark => mark.Name == "fixture.handoff").Select(mark => mark.Label).SequenceEqual(
            new[] { "Install self-test loopback / supplies off / 0 active routes", "Connect the DUT / supplies off / 0 active routes" }),
            "Fixture handoffs did not establish the expected supplies-off sequence.");
        Check(full.Events.Count(mark => mark.Name == "tables") == 1
              && full.Events.Count(mark => mark.Name == "case.route" && mark.Label?.StartsWith("rail-", StringComparison.Ordinal) == true) == 3,
            "Setup or station profile validation repeated within the run.");

        var rows = full.Samples.Where(sample => sample.RowVerdict is not null).ToArray();
        Check(rows.Length > 100 && rows.All(row => row.RowVerdict == "Pass" && row.Timestamp.Offset == TimeSpan.Zero
            && row.LimitLow != null && row.LimitHigh != null && row.StepRunId.HasValue), "Evaluated rows lost evidence.");
        Check(rows.Select(row => row.MetricKey).Distinct().Count() == rows.Length, "A result key collapsed multiple evaluated rows.");
        var score = rows.Single(row => row.MetricKey == "diff-response.curveScore");
        Check(score.LimitLow == 93 && score.LimitHigh == 99.9, "Score bounds were not preserved.");
        var captured = full.Samples.Where(sample => sample.StepRunId == score.StepRunId && sample.SampleIndex.HasValue).ToArray();
        Check(captured.Select(sample => sample.MetricKey).Distinct().Count() >= 20, "Shared execution lost capture dimensions.");
        Check(captured.GroupBy(sample => sample.MetricKey).All(trace =>
            trace.OrderBy(sample => sample.SampleIndex).First().Timestamp < trace.OrderBy(sample => sample.SampleIndex).Last().Timestamp),
            "Capture timestamps were collapsed at ingestion.");

        var reversed = await RespondAsync(worker, ["Loopback", "Terminated", "Crossed"], false, timeout.Token);
        Check(reversed.Result == RunResult.Passed && reversed.Events.Where(mark => mark.Name == "fixture.configuration")
            .Select(mark => mark.Label).SequenceEqual(new[] { "Loopback", "Terminated", "Crossed" }),
            "The next run did not reset configuration state.");
        await VerifyGatesAndBoundsAsync(worker, programPath, full, settings, timeout.Token);
        await SaveReportsAsync(full, settings, timeout.Token);
        Console.WriteLine($"Worker verified {rows.Length} evaluated rows and {full.Samples.Count - rows.Length} captured samples.");
    }

    public static async Task<int> SimulateAsync(string appDirectory, string dataDirectory, string[] responses)
    {
        var programPath = Path.GetFullPath(Path.Combine(appDirectory, "Programs", ProcedurePlan.PlanName));
        var workerPath = Path.GetFullPath(Path.Combine(appDirectory, OperatingSystem.IsWindows()
            ? "HardwareTest.OpenTap.Worker.exe" : "HardwareTest.OpenTap.Worker"));
        var settings = new AppSettings
        {
            DataDirectory = Path.GetFullPath(dataDirectory),
            OpenTapPluginDirectories = [Path.GetFullPath(Path.Combine(dataDirectory, "plugins", "AdvancedProcedure"))],
            UseMockVisa = true, ExportOpenTapResults = true, CrashEnabled = false,
        };
        using var worker = new OpenTapWorkerClient(settings, executablePath: workerPath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await worker.LoadPlanAsync(programPath, timeout.Token);
        await worker.ApplyStationAndDutAsync(new StationProfile(new Dictionary<string, string>()),
            new DutIdentity("EXAMPLE-01", null, null, "demo"), timeout.Token);
        var run = await RespondAsync(worker, responses, cancelAtConfiguration: false, timeout.Token);
        Console.WriteLine($"{run.Result} · {run.ErrorMessage}");
        foreach (var row in run.Samples.Where(row => row.RowVerdict == "Fail"))
            Console.WriteLine(row.ToDisplayLine());
        if (run.Samples.Any(row => row.RowVerdict is not null)) await SaveReportsAsync(run, settings, timeout.Token);
        return run.Result == RunResult.Passed ? 0 : 1;
    }

    private static async Task VerifyGatesAndBoundsAsync(
        OpenTapWorkerClient worker, string sourcePlan, OpenTapRunSummary baseline, AppSettings settings,
        CancellationToken cancellationToken)
    {
        var temp = Directory.CreateTempSubdirectory("hardwaretest-edited-tables-");
        try
        {
            var sourceTables = Path.Combine(Path.GetDirectoryName(sourcePlan)!, "tables", "advanced-procedure");
            var targetTables = temp.CreateSubdirectory(Path.Combine("tables", "advanced-procedure")).FullName;
            var stationPath = Path.Combine(targetTables, "station.json");
            var testPath = Path.Combine(targetTables, "tests.json");
            File.Copy(Path.Combine(sourceTables, "tests.json"), testPath);
            var station = ReadTable("station.json");
            var plan = Path.Combine(temp.FullName, ProcedurePlan.PlanName);
            File.Copy(sourcePlan, plan);
            await worker.LoadPlanAsync(plan, cancellationToken);
            var leak = Row(station, "routes", "rail-logic-leak");
            var normalLeak = leak["nominal"]!.GetValue<double>();
            leak["nominal"] = 0.5;
            File.WriteAllText(stationPath, station.ToJsonString());

            var failure = await worker.RunAsync(cancellationToken: cancellationToken);
            var failedRow = failure.Samples.Single(row => row.MetricKey == "rail-logic.groundLeak");
            Check(failure.Result == RunResult.Failed && failedRow.RowVerdict == "Fail" && failedRow.Value == 0.5
                && failedRow.LimitHigh == 0.08, "The failed rail limit was not preserved.");
            Check(HasFixtureReset(failure)
                && failure.Events.Count(mark => mark.Name == "case.route") == 1
                && !failure.Events.Any(mark => mark.Name == "fixture.confirmed"),
                "A failed station prerequisite did not gate dependent suites or reset the fixture.");
            Console.WriteLine("Station gate blocked downstream cases and recorded reset.");

            leak["nominal"] = normalLeak;
            var clock = Row(station, "routes", "dut-clock-lo");
            var normalClock = clock["nominal"]!.GetValue<double>();
            clock["nominal"] = 0.6;
            File.WriteAllText(stationPath, station.ToJsonString());
            var dutFailure = await RespondAsync(worker, [], false, cancellationToken);
            Check(dutFailure.Result == RunResult.Failed &&
                dutFailure.Samples.Any(row => row.MetricKey == "dut-clock.low" && row.RowVerdict == "Fail") &&
                dutFailure.Samples.Any(row => row.MetricKey == "dut-clock.high" && row.RowVerdict == "Pass") &&
                !dutFailure.Samples.Any(row => row.MetricKey.StartsWith("diff-", StringComparison.Ordinal)) &&
                HasFixtureReset(dutFailure), "A failed DUT prerequisite did not gate or reset.");
            Console.WriteLine("DUT gate blocked analog suites after both fixture handoffs.");

            clock["nominal"] = normalClock;
            File.WriteAllText(stationPath, station.ToJsonString());
            var tests = ReadTable("tests.json");
            var lower = Row(Row(tests, "cases", "diff-response"), "criteria", "curveScore");
            var upper = Row(Row(tests, "cases", "diff-margin"), "criteria", "curveScore");
            var lowerLimits = baseline.Samples.Single(row => row.MetricKey == "diff-response.curveScore");
            var upperLimits = baseline.Samples.Single(row => row.MetricKey == "diff-margin.curveScore");
            lower["low"] = lowerLimits.Value;
            upper["low"] = 90;
            upper["high"] = upperLimits.Value;
            File.WriteAllText(testPath, tests.ToJsonString());
            var choices = new[] { "Terminated", "Loopback", "Crossed" };
            var equality = await RespondAsync(worker, choices, false, cancellationToken);
            Check(equality.Result == RunResult.Passed && equality.Samples.Any(row => row.MetricKey == lowerLimits.MetricKey
                && row.RowVerdict == "Pass" && row.Value == row.LimitLow) && equality.Samples.Any(row =>
                    row.MetricKey == upperLimits.MetricKey && row.RowVerdict == "Pass" && row.Value == row.LimitHigh),
                "Inclusive bounds or rerun after prerequisite failure failed.");

            lower["low"] = lowerLimits.Value + 0.001;
            upper["high"] = upperLimits.Value - 0.001;
            File.WriteAllText(testPath, tests.ToJsonString());
            var outside = await RespondAsync(worker, choices, false, cancellationToken);
            Check(outside.Result == RunResult.Failed && outside.Samples.Any(row => row.MetricKey == lowerLimits.MetricKey
                && row.RowVerdict == "Fail" && row.Value < row.LimitLow) && outside.Samples.Any(row =>
                    row.MetricKey == upperLimits.MetricKey && row.RowVerdict == "Fail" && row.Value > row.LimitHigh)
                && outside.Events.Count(mark => mark.Name == "fixture.configuration") == 3,
                "Edited bounds did not produce mixed results while preserving independent fixture coverage.");
            await SaveReportsAsync(outside, settings, cancellationToken);
            Console.WriteLine("Edited bounds passed at both endpoints and failed just outside each endpoint.");

            Row(tests, "cases", "rail-logic")["routes"]![0] = "missing-matrix-route";
            File.WriteAllText(testPath, tests.ToJsonString());
            try
            {
                _ = HardwareTest.Examples.AdvancedProcedure.ProcedureTables.Load(targetTables);
                throw new InvalidOperationException("Malformed route unexpectedly passed inspection.");
            }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains("rail-logic", StringComparison.Ordinal), "Invalid route diagnosis omitted the case ID.");
                Console.WriteLine($"Malformed route rejected during inspection. {error.Message}");
            }
            var invalid = await worker.RunAsync(cancellationToken: cancellationToken);
            Check(invalid.Result == RunResult.Error && invalid.Samples.Count == 0, "Malformed route reached measurement.");

            JsonNode ReadTable(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(sourceTables, name)))!;
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private static JsonNode Row(JsonNode parent, string array, string id)
        => parent[array]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;

    private static async Task SaveReportsAsync(OpenTapRunSummary summary, AppSettings settings, CancellationToken cancellationToken)
    {
        var store = new FileRunStore(Path.Combine(settings.DataDirectory, "runs"));
        var record = new TestRunRecord
        {
            RunId = summary.RunId,
            PlanId = "advanced-procedure",
            PlanName = summary.PlanName,
            DutSerial = summary.DutSerial,
            OperatorName = "Example verifier",
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            Result = summary.Result,
            Steps = summary.Steps,
            Events = summary.Events,
            Samples = summary.Samples,
        };
        await store.SaveAsync(record, cancellationToken);
        var saved = await store.LoadAsync(record.RunId, cancellationToken);
        Check(saved?.Samples.Count == record.Samples.Count
              && saved.Samples.Zip(record.Samples).All(pair => pair.First.Timestamp == pair.Second.Timestamp
                  && pair.First.RowVerdict == pair.Second.RowVerdict), "run.json changed row timestamps or verdicts.");
        var runDirectory = store.GetRunDirectory(record.RunId);
        foreach (var table in new[] { "Capture.csv", "Evaluated.csv" })
            Check(Directory.EnumerateFiles(runDirectory, table, SearchOption.AllDirectories).Any(), $"Missing {table}.");

        using var reports = new TypstReportService(store, settings);
        var artifacts = await reports.GenerateReportsAsync(record, [ReportKinds.Status, ReportKinds.Certification],
            history: null, cancellationToken);
        Check(artifacts.Count == 2 && artifacts.All(report => new FileInfo(report.PdfPath).Length > 0), "Report generation failed.");
        Console.WriteLine($"Run evidence and PDFs in {runDirectory}");
    }

    private static async Task<OpenTapRunSummary> RespondAsync(
        OpenTapWorkerClient worker, IEnumerable<string> choices, bool cancelAtConfiguration, CancellationToken cancellationToken,
        bool abortAfterConfigurationStarts = false)
    {
        using var inputs = choices.GetEnumerator();
        var requests = new HashSet<string>(StringComparer.Ordinal);
        var configured = 0;
        var run = worker.RunAsync(new CallbackProgress(progress =>
        {
            if (abortAfterConfigurationStarts && progress.Event?.Name == "fixture.configuration.started")
                Interlocked.Exchange(ref configured, 1);
        }), cancellationToken);
        while (!run.IsCompleted)
        {
            if (Interlocked.Exchange(ref configured, 0) == 1)
                worker.Abort();

            if (worker.PendingInteraction is { } request && requests.Add(request.Id))
            {
                if (request.Fields.Count == 0)
                {
                    worker.Resume(OperatorInteractionResponse.Continue(request.Id));
                }
                else if (cancelAtConfiguration)
                {
                    worker.Resume(OperatorInteractionResponse.Cancel(request.Id));
                }
                else
                {
                    Check(inputs.MoveNext(), $"Unexpected operator request: {request.Message}");
                    worker.Resume(OperatorInteractionResponse.Continue(request.Id, new() { [request.Fields.Single().Id] = inputs.Current }));
                }
            }

            await Task.WhenAny(run, Task.Delay(15, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await run;
    }

    private sealed class CallbackProgress(Action<OpenTapProgress> receive) : IProgress<OpenTapProgress>
    {
        public void Report(OpenTapProgress value) => receive(value);
    }

    private static bool HasFixtureReset(OpenTapRunSummary summary)
        => summary.Events.Any(mark => mark.Name == "fixture.reset" && mark.Label == "supplies off / 0 active routes");

    private static IEnumerable<OpenTapStepNode> Flatten(OpenTapStepNode node)
        => node.Children.SelectMany(Flatten).Prepend(node);

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
