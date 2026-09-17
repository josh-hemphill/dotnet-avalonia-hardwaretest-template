using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace HardwareTest.Authoring;

/// Maps sequence depth pixels to a left-only Thickness. Not a TreeView indent.
public sealed class LeftIndentConverter : IValueConverter
{
    public static LeftIndentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int indent
            ? new Thickness(indent, 2, 0, 2)
            : new Thickness(0, 2, 0, 2);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
