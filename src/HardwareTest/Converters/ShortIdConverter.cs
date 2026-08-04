using System;
using System.Globalization;
using Avalonia.Data.Converters;
using HardwareTest.Core.Text;

namespace HardwareTest.Converters;

/// Avalonia binding converter for operator-facing short ids.
public sealed class ShortIdConverter : IValueConverter
{
    public static ShortIdConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var length = ShortId.DefaultLength;
        if (parameter is int i && i > 0)
        {
            length = i;
        }
        else if (parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            length = parsed;
        }

        return ShortId.Display(value as string, length);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
