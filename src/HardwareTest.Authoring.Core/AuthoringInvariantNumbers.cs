using System.Globalization;

namespace HardwareTest.Authoring;

/// Invariant parsing for inspector numeric/bool settings.
public static class AuthoringInvariantNumbers
{
    public static bool TryParseBool(string? raw, out bool value)
        => bool.TryParse(raw, out value);

    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = (decimal)parsed;
        return true;
    }

    public static string FormatBool(bool value)
        => value ? "true" : "false";

    public static string FormatInt(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string FormatDecimal(decimal value)
        => decimal.Truncate(value) == value
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
}
