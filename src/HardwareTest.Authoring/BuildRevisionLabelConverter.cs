using System.Globalization;
using Avalonia.Data.Converters;

namespace HardwareTest.Authoring;

public sealed class BuildRevisionLabelConverter : IValueConverter
{
    public static BuildRevisionLabelConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long revision ? $"Saved source revision {revision}" : "Compiled input only — no editable source revision";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
