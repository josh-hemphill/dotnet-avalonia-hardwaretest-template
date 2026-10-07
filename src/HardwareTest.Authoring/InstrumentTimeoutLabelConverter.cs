using System.Globalization;
using Avalonia.Data.Converters;

namespace HardwareTest.Authoring;

public sealed class InstrumentTimeoutLabelConverter : IValueConverter
{
    public static InstrumentTimeoutLabelConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is AuthoringInstrumentAdapter adapter
            ? adapter.ConfigurationFields.Contains("IoTimeoutMilliseconds")
                ? $"I/O timeout — {adapter.DisplayName} (ms)"
                : $"I/O timeout — unsupported by {adapter.DisplayName}"
            : "I/O timeout — select an instrument type";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
