using Avalonia.Data.Converters;

namespace HardwareTest.Features.RunTest;

/// String visibility for step-row subtitles — whitespace-only values must not take layout.
public static class StringPresenceConverter
{
    public static readonly IValueConverter IsNotNullOrWhiteSpace =
        new FuncValueConverter<string?, bool>(value => !string.IsNullOrWhiteSpace(value));
}
