using System.Diagnostics;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

public abstract class FixtureMeasurementStep : RuntimeAwareTestStep, IEvaluatedResultStep
{
    public string CaseId { get; set; } = string.Empty;

    internal abstract (string Id, string Unit)[] Criteria { get; }

    internal void Validate(ProcedureTables tables)
    {
        var row = tables.Case(CaseId);
        var expected = Criteria.ToDictionary(criterion => criterion.Id, criterion => criterion.Unit);
        ProcedureTables.Require(row.Criteria.Length == expected.Count && row.Criteria.All(band =>
            expected.TryGetValue(band.Id, out var unit) && unit == band.Unit),
            $"Case '{row.Id}' must declare these criteria and units: {string.Join(", ", Criteria.Select(item => $"{item.Id} ({item.Unit})"))}.");
        Validate(tables, row);
    }

    internal virtual void Validate(ProcedureTables tables, CaseTable row) { }

    internal abstract void Measure(FixtureProcedureStep fixture, CaseTable row, MeasurementEvidence evidence);

    public sealed override void Run()
    {
        WaitIfPaused();
        TapThread.ThrowIfAborted();
        var fixture = FixtureProcedureStep.FindRoot(this);
        var tables = fixture.Tables;
        var row = tables.Case(CaseId);
        var started = Stopwatch.StartNew();
        fixture.SelectRoutes(row);
        FixtureProcedureStep.RecordEvent(Results, "case.route", started, tables.Provenance(row));
        if (fixture.ActiveConfiguration is { } configuration)
            FixtureProcedureStep.RecordEvent(Results, "case.configuration", started, configuration);

        var evidence = new MeasurementEvidence(row);
        Measure(fixture, row, evidence);
        evidence.Publish(Results);
        UpgradeVerdict(evidence.Passed ? Verdict.Pass : Verdict.Fail);
    }

    internal static Route[] RequireRoutes(ProcedureTables tables, CaseTable row, params string[] units)
    {
        var routes = tables.Routes(row);
        ProcedureTables.Require(routes.Select(route => route.Unit).SequenceEqual(units),
            $"Case '{row.Id}' requires one route for each of these units, in order: {string.Join(", ", units)}.");
        return routes;
    }

    internal static void RequireLevels(CaseTable row, int min, int? max = null)
        => ProcedureTables.Require(row.Levels.Length >= min && (max is null || row.Levels.Length <= max),
            $"Case '{row.Id}' needs {min} to {max?.ToString() ?? "any"} stimulus values.");
}

internal sealed class MeasurementEvidence(CaseTable row)
{
    private sealed record CapturePoint(string Key, string Unit, int Index, double ElapsedMs, double Value, string Timestamp);
    private sealed record Evaluation(string Key, Band Band, double Value, bool Passed, string Timestamp);
    private readonly List<CapturePoint> _capture = [];
    private readonly List<Evaluation> _evaluations = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public bool Passed => _evaluations.All(result => result.Passed);

    public void Evaluate(string criterion, double value, string unit, string? coordinate = null)
    {
        var band = row.Criteria.SingleOrDefault(item => item.Id == criterion)
            ?? throw new InvalidDataException($"Case '{row.Id}' has no criterion '{criterion}'.");
        ProcedureTables.Require(band.Unit == unit, $"Case '{row.Id}' criterion '{criterion}' expects {unit}, found {band.Unit}.");
        ProcedureTables.Require(double.IsFinite(value), $"Case '{row.Id}' produced a nonfinite {criterion}.");
        var key = coordinate is null ? $"{row.Id}.{criterion}" : $"{row.Id}.{coordinate}.{criterion}";
        ProcedureTables.Require(_keys.Add(key), $"Case '{row.Id}' emitted duplicate metric '{key}'.");
        _evaluations.Add(new(key, band, value, value >= band.Low && value <= band.High,
            FixtureProcedureStep.Timestamp(DateTimeOffset.UtcNow)));
    }

    public void Trace(string coordinate, string unit, double[] values, double intervalMs, DateTimeOffset lastSample)
    {
        var key = $"{row.Id}.capture.{coordinate}";
        for (var i = 0; i < values.Length; i++)
        {
            var elapsed = i * intervalMs;
            var timestamp = lastSample.AddMilliseconds((i - values.Length + 1) * intervalMs);
            _capture.Add(new(key, unit, i, elapsed, values[i], FixtureProcedureStep.Timestamp(timestamp)));
        }
    }

    public void Publish(ResultSource results)
    {
        var evaluated = _evaluations.Select(result => result.Band.Id).ToHashSet(StringComparer.Ordinal);
        ProcedureTables.Require(row.Criteria.All(band => evaluated.Contains(band.Id)), $"Case '{row.Id}' did not evaluate every declared criterion.");
        if (_capture.Count > 0)
        {
            results.PublishTable("Capture", ["Channel", "Index", "Value", "Unit", "ElapsedMs", "TimestampUtc"],
                _capture.Select(item => item.Key).ToArray(),
                _capture.Select(item => item.Index).ToArray(),
                _capture.Select(item => item.Value).ToArray(),
                _capture.Select(item => item.Unit).ToArray(),
                _capture.Select(item => item.ElapsedMs).ToArray(),
                _capture.Select(item => item.Timestamp).ToArray());
        }

        results.PublishTable("Evaluated", ["Name", "Value", "Unit", "LimitLow", "LimitHigh", "Verdict", "TimestampUtc", "DisplayName"],
            _evaluations.Select(item => item.Key).ToArray(),
            _evaluations.Select(item => item.Value).ToArray(),
            _evaluations.Select(item => item.Band.Unit).ToArray(),
            _evaluations.Select(item => item.Band.Low).ToArray(),
            _evaluations.Select(item => item.Band.High).ToArray(),
            _evaluations.Select(item => item.Passed ? "Pass" : "Fail").ToArray(),
            _evaluations.Select(item => item.Timestamp).ToArray(),
            _evaluations.Select(item => item.Band.DisplayName ?? string.Empty).ToArray());
    }
}
