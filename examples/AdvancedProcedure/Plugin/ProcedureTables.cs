using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardwareTest.Examples.AdvancedProcedure;

internal sealed record Route(string Id, string Path, string Unit, double Nominal, double[]? CounterReadings = null);
internal sealed record Calibration(string Route, double ImpedanceOhms, double CorrectionDb, double? SimulatedVoltageScale = null);
internal sealed record Band(string Id, string Unit, double Low, double High, string? DisplayName = null);
internal sealed record ReferenceTrace(string Id, double[] Values);
internal sealed record CaseTable(string Id, string[] Routes, double[] Levels, string? Reference, Band[] Criteria);
internal sealed record StationTable(int SchemaVersion, string StationId, string Revision, string CalibrationRevision,
    Route[] Routes, Calibration[] Calibrations);
internal sealed record TestTable(int SchemaVersion, string StationId, string Revision, ReferenceTrace[] References, CaseTable[] Cases);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StationTable))]
[JsonSerializable(typeof(TestTable))]
internal sealed partial class ProcedureJsonContext : JsonSerializerContext;

public sealed class ProcedureTables
{
    private readonly StationTable _station;
    private readonly TestTable _tests;
    private readonly IReadOnlyDictionary<string, Route> _routes;
    private readonly IReadOnlyDictionary<string, ReferenceTrace> _references;
    private readonly IReadOnlyDictionary<string, CaseTable> _cases;
    private readonly string _stationHash;
    private readonly string _testHash;

    private ProcedureTables(StationTable station, TestTable tests, string stationHash, string testHash)
    {
        _station = station;
        _tests = tests;
        _stationHash = stationHash;
        _testHash = testHash;
        Require(station.SchemaVersion == 1 && tests.SchemaVersion == 1, "Expected schemaVersion 1 in both tables.");
        Require(!string.IsNullOrWhiteSpace(station.StationId) && tests.StationId == station.StationId,
            "station.json and tests.json must target the same stationId.");
        Require(!string.IsNullOrWhiteSpace(station.Revision) && !string.IsNullOrWhiteSpace(station.CalibrationRevision)
            && !string.IsNullOrWhiteSpace(tests.Revision), "Both tables and the calibration require revisions.");

        _routes = Index(station.Routes, row => row.Id, "station.json routes");
        _references = Index(tests.References, row => row.Id, "tests.json references");
        _cases = Index(tests.Cases, row => row.Id, "tests.json cases");
        Require(_routes.Count > 0 && _cases.Count > 0, "The route and case tables must contain rows.");
        foreach (var route in _routes.Values)
            Require(!string.IsNullOrWhiteSpace(route.Path) && !string.IsNullOrWhiteSpace(route.Unit)
                && double.IsFinite(route.Nominal), $"Route '{route.Id}' requires a path, unit and finite nominal value.");
        var calibrationKeys = new HashSet<(string, double)>();
        foreach (var calibration in station.Calibrations ?? throw new InvalidDataException("Missing station.json calibrations."))
        {
            Require(_routes.ContainsKey(calibration.Route) && Positive(calibration.ImpedanceOhms)
                && double.IsFinite(calibration.CorrectionDb) && Positive(calibration.SimulatedVoltageScale ?? 1)
                && calibrationKeys.Add((calibration.Route, calibration.ImpedanceOhms)),
                $"Invalid or duplicate calibration for {calibration.Route} at {calibration.ImpedanceOhms} ohms.");
        }

        foreach (var reference in _references.Values)
            Require(reference.Values is { Length: > 1 } && reference.Values.All(double.IsFinite),
                $"Reference '{reference.Id}' needs at least two finite points.");
        foreach (var row in _cases.Values)
        {
            Require(row.Routes is { Length: > 0 }, $"Case '{row.Id}' needs a station route.");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in row.Routes)
            {
                if (string.IsNullOrWhiteSpace(route) || !_routes.ContainsKey(route))
                    throw new InvalidDataException($"Case '{row.Id}' references missing station route '{route ?? "(empty)"}'.");
                Require(used.Add(route), $"Case '{row.Id}' repeats station route '{route}'.");
            }
            Require(row.Levels is { Length: > 0 } && row.Levels.All(double.IsFinite), $"Case '{row.Id}' needs finite stimulus values.");
            Require(row.Reference is null || _references.ContainsKey(row.Reference), $"Case '{row.Id}' references unknown trace '{row.Reference}'.");
            var bands = Index(row.Criteria, item => item.Id, $"tests.json criteria for '{row.Id}'");
            Require(bands.Count > 0, $"Case '{row.Id}' needs criteria.");
            foreach (var band in bands.Values)
                Require(!string.IsNullOrWhiteSpace(band.Unit) && double.IsFinite(band.Low)
                    && double.IsFinite(band.High) && band.Low <= band.High,
                    $"Case '{row.Id}' has an invalid band for '{band.Id}'.");
        }
    }

    public static ProcedureTables Load(string directory)
    {
        var stationPath = Path.Combine(directory, "station.json");
        var testsPath = Path.Combine(directory, "tests.json");
        var station = File.ReadAllBytes(stationPath);
        var tests = File.ReadAllBytes(testsPath);
        var context = new ProcedureJsonContext(new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        });
        return new ProcedureTables(
            Read(station, context.StationTable, stationPath), Read(tests, context.TestTable, testsPath),
            Convert.ToHexStringLower(SHA256.HashData(station)), Convert.ToHexStringLower(SHA256.HashData(tests)));
    }

    public string DescribeProfile()
        => $"Station {_station.StationId} / routes {_station.Revision} / calibration {_station.CalibrationRevision} / tests {_tests.Revision}";

    public void Describe(TextWriter writer)
    {
        writer.WriteLine(DescribeProfile());
        writer.WriteLine($"station.json SHA-256 {_stationHash}");
        writer.WriteLine($"tests.json SHA-256 {_testHash}");
        foreach (var row in _cases.Values)
        {
            writer.WriteLine($"{row.Id}  levels [{FormatNumbers(row.Levels)}]");
            if (row.Reference is { } reference)
                writer.WriteLine($"  reference {reference} [{FormatNumbers(_references[reference].Values)}]");
            foreach (var route in Routes(row))
            {
                writer.WriteLine(FormattableString.Invariant($"  {route.Id}  {route.Path}  nominal {route.Nominal:G} {route.Unit}"));
                if (route.CounterReadings is { } readings)
                    writer.WriteLine($"    recorded counter readings [{FormatNumbers(readings)}]");
                foreach (var calibration in _station.Calibrations.Where(item => item.Route == route.Id))
                    writer.WriteLine(DescribeCalibration(calibration));
            }
            foreach (var band in row.Criteria)
                writer.WriteLine(FormattableString.Invariant($"  {band.Id}  [{band.Low:G}, {band.High:G}] {band.Unit}"));
        }
    }

    private static string FormatNumbers(IEnumerable<double> values)
        => string.Join(", ", values.Select(value => FormattableString.Invariant($"{value:G}")));

    internal CaseTable Case(string id)
        => _cases.TryGetValue(id, out var row) ? row : throw new InvalidDataException($"No case '{id}' in tests.json.");

    internal Route[] Routes(CaseTable row) => [.. row.Routes.Select(id => _routes[id])];

    internal string Provenance(CaseTable row)
        => $"{row.Id} / {string.Join("; ", Routes(row).Select(route => FormattableString.Invariant($"{route.Id} = {route.Path} ({route.Nominal:G} {route.Unit})")))}"
           + $" / stimuli [{FormatNumbers(row.Levels)}]"
           + (row.Reference is null ? string.Empty : $" / reference {row.Reference}")
           + string.Concat(_station.Calibrations.Where(item => row.Routes.Contains(item.Route)).Select(item => $" / {DescribeCalibration(item)}"));

    private static string DescribeCalibration(Calibration row)
        => FormattableString.Invariant($"{row.Route} at {row.ImpedanceOhms:G} Ω correction {row.CorrectionDb:G} dB / simulated voltage scale {row.SimulatedVoltageScale ?? 1:G}");

    internal string FileHashes() => $"station.json {_stationHash}; tests.json {_testHash}";

    internal double[] Reference(CaseTable row)
        => row.Reference is { } id ? _references[id].Values : throw new InvalidDataException($"Case '{row.Id}' needs a reference.");

    internal Calibration CalibrationFor(Route route, double impedance)
        => _station.Calibrations.SingleOrDefault(row => row.Route == route.Id && row.ImpedanceOhms == impedance)
           ?? throw new InvalidDataException(FormattableString.Invariant($"No calibration for '{route.Id}' at {impedance:G} Ω."));

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private static bool Positive(double number) => double.IsFinite(number) && number > 0;

    private static Dictionary<string, T> Index<T>(IEnumerable<T>? entries, Func<T, string> id, string source) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var entry in entries ?? throw new InvalidDataException($"Missing {source}."))
        {
            if (entry is null || string.IsNullOrWhiteSpace(id(entry)) || !result.TryAdd(id(entry), entry))
                throw new InvalidDataException($"Missing or duplicate row id '{(entry is null ? null : id(entry))}' in {source}.");
        }

        return result;
    }

    private static T Read<T>(byte[] content, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, string path)
    {
        try
        {
            return JsonSerializer.Deserialize(content, type) ?? throw new InvalidDataException($"Empty table {path}.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Invalid table {path}: {error.Message}", error);
        }
    }
}
