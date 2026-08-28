using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace HardwareTest.Features.RunTest;

/// <summary>Maps a bool to 1.0 when true and 0.0 when false so reserved chrome can stay laid out while hidden.</summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static BoolToOpacityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1d : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
