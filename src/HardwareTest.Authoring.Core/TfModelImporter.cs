using System.Text.Json;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

/// Discrete TF import result. OutputChannelKey becomes MetricDraft.ChannelKey.
public sealed record TfImport(
    TransferFunctionAlgorithm Algorithm,
    string OutputChannelKey);

/// Coefficient JSON document (schemaVersion 1).
public sealed class TfModelJson
{
    public int SchemaVersion { get; set; }

    public double TsSeconds { get; set; }

    public double[] Numerator { get; set; } = [];

    public double[] Denominator { get; set; } = [];

    public string Method { get; set; } = "filter";

    public string TimeBase { get; set; } = "elapsedMs";

    public string InputChannelKey { get; set; } = string.Empty;

    public string OutputChannelKey { get; set; } = string.Empty;

    public string InitialConditions { get; set; } = "zero";
}

/// Coefficient JSON interchange. Not MATLAB source.
public static class TfModelImporter
{
    private static readonly HashSet<string> RequiredKeys = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "tsSeconds",
        "numerator",
        "denominator",
        "method",
        "timeBase",
        "inputChannelKey",
        "outputChannelKey",
        "initialConditions",
    };

    /// Loads TfModelJson. Fail closed on omitted keys, extra properties, or illegal values.
    public static TfImport Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new AuthoringWorkspaceException($"TF model not found: {full}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(full));
        }
        catch (Exception ex)
        {
            throw new AuthoringWorkspaceException($"Invalid TF JSON at {full}: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw FailImport("document must be a JSON object");
            }

            foreach (var prop in document.RootElement.EnumerateObject())
            {
                if (!RequiredKeys.Contains(prop.Name))
                {
                    throw FailImport($"unknown property '{prop.Name}'");
                }
            }

            foreach (var key in RequiredKeys)
            {
                if (!document.RootElement.TryGetProperty(key, out _))
                {
                    throw FailImport($"missing '{key}'");
                }
            }

            var schema = document.RootElement.GetProperty("schemaVersion").GetInt32();
            if (schema != 1)
            {
                throw FailImport($"schemaVersion {schema} is not 1");
            }

            var ts = document.RootElement.GetProperty("tsSeconds").GetDouble();
            if (ts <= 0)
            {
                throw FailImport("tsSeconds must be > 0");
            }

            var numerator = ReadVector(document.RootElement.GetProperty("numerator"), "numerator");
            var denominator = ReadVector(document.RootElement.GetProperty("denominator"), "denominator");
            var method = document.RootElement.GetProperty("method").GetString() ?? string.Empty;
            if (method is not ("filter" or "filtfilt"))
            {
                throw FailImport($"method '{method}' is not filter|filtfilt");
            }

            var timeBase = document.RootElement.GetProperty("timeBase").GetString() ?? string.Empty;
            if (!string.Equals(timeBase, "elapsedMs", StringComparison.Ordinal))
            {
                throw FailImport($"timeBase '{timeBase}' is not elapsedMs");
            }

            var initial = document.RootElement.GetProperty("initialConditions").GetString() ?? string.Empty;
            if (!string.Equals(initial, "zero", StringComparison.Ordinal))
            {
                throw FailImport($"initialConditions '{initial}' is not zero");
            }

            var input = document.RootElement.GetProperty("inputChannelKey").GetString() ?? string.Empty;
            var output = document.RootElement.GetProperty("outputChannelKey").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            {
                throw FailImport("inputChannelKey and outputChannelKey are required");
            }

            var (num, den) = NormalizeOrThrow(numerator, denominator);
            return new TfImport(
                new TransferFunctionAlgorithm(input, num, den, ts, method),
                output);
        }
    }

    /// Writes a v1 TfModelJson document.
    public static void Save(string path, TfImport model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(model);
        var document = new TfModelJson
        {
            SchemaVersion = 1,
            TsSeconds = model.Algorithm.TsSeconds,
            Numerator = model.Algorithm.Numerator.ToArray(),
            Denominator = model.Algorithm.Denominator.ToArray(),
            Method = model.Algorithm.Method,
            TimeBase = "elapsedMs",
            InputChannelKey = model.Algorithm.InputChannelKey,
            OutputChannelKey = model.OutputChannelKey,
            InitialConditions = "zero",
        };
        var json = JsonSerializer.Serialize(document, AuthoringJsonContext.Default.TfModelJson);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
    }

    private static IReadOnlyList<double> ReadVector(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw FailImport($"{name} must be a non-empty array");
        }

        return element.EnumerateArray().Select(item => item.GetDouble()).ToArray();
    }

    private static (double[] Num, double[] Den) NormalizeOrThrow(
        IReadOnlyList<double> numerator,
        IReadOnlyList<double> denominator)
    {
        try
        {
            var (b, a) = TransferFunctionFilter.Normalize(numerator, denominator);
            return (b, a);
        }
        catch (InvalidOperationException ex)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.TfDenLeadingZero}: {ex.Message}",
                ex);
        }
    }

    private static AuthoringWorkspaceException FailImport(string message)
        => new($"{AuthoringCompileCodes.TfImport}: {message}.");
}

