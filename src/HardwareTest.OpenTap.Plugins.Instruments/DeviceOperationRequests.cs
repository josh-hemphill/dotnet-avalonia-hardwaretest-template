using System.Globalization;
using InstrumentComponents.Drivers.Catalog;

namespace HardwareTest.OpenTap.Plugins.Instruments;

public static partial class DeviceOperationRequests
{
    public static CatalogRequest Create(string operation, string arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in arguments.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || !values.TryAdd(pair[0], pair[1]))
                throw new ArgumentException("Use one unique name=value argument per line.");
        }
        var request = Construct(operation, values);
        if (values.Count > 0) throw new ArgumentException("Unknown arguments: " + string.Join(", ", values.Keys));
        request.Validate();
        return request;
    }

    public static string Template(string operation)
    {
        var metadata = Enum.Parse<CatalogOperation>(operation).Describe();
        return string.Join("; ", metadata.Parameters.Where(parameter => parameter.Kind != ParameterKind.OptionalNumber).Select(parameter =>
            parameter.Name + "=" + (parameter.Kind switch
            {
                ParameterKind.Boolean => "false",
                ParameterKind.Waveform => "Sine",
                ParameterKind.PowerUnit => "Watt",
                ParameterKind.SenseMode => "Internal",
                ParameterKind.Symbol => "EDIT",
                ParameterKind.Number when parameter.Constraint == "positive" => "1",
                ParameterKind.Number => "0",
                _ => "1"
            })));
    }

    private static string Text(Dictionary<string, string> values, string name)
        => values.Remove(name, out var text) && !string.IsNullOrWhiteSpace(text) ? text : throw new ArgumentException($"Enter {name}.");
    private static double Number(Dictionary<string, string> values, string name)
        => double.Parse(Text(values, name), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static double? Optional(Dictionary<string, string> values, string name)
        => values.Remove(name, out var text) && !string.IsNullOrWhiteSpace(text) ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) : null;
    private static uint Unsigned(Dictionary<string, string> values, string name)
        => uint.Parse(Text(values, name), NumberStyles.None, CultureInfo.InvariantCulture);
    private static bool Boolean(Dictionary<string, string> values, string name)
        => bool.Parse(Text(values, name));
    private static T EnumValue<T>(Dictionary<string, string> values, string name) where T : struct, Enum
        => Enum.TryParse<T>(Text(values, name), ignoreCase: false, out var value) && Enum.IsDefined(value)
            ? value : throw new ArgumentException($"Choose a valid {name}: {string.Join(", ", Enum.GetNames<T>())}.");
}
