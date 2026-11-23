using System.Globalization;

namespace HardwareTest.Widgets.Presentation;

/// A result and its applicable limits, with the distance to the deciding boundary.
public sealed record LimitReadout(string Lower, string Value, string Upper, string Distance)
{
    public static LimitReadout? Create(double value, double? lower, double? upper, string? unit)
    {
        if (lower is null && upper is null)
        {
            return null;
        }

        // Use extra precision when rounding would make a near miss look like an exact hit.
        var numbers = new double?[] { lower, value, upper }.OfType<double>().Distinct().ToArray();
        var format = numbers.Select(number => Format(number, "G10")).Distinct().Count() == numbers.Length ? "G10" : "G";
        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        string Reading(double? number) => number is { } n && double.IsFinite(n) ? Format(n, format) + suffix : "—";
        string Gap(double difference) => Format(difference, "G4") + suffix;

        string distance;
        if (lower is { } low && !double.IsFinite(low) || upper is { } high && !double.IsFinite(high) || lower > upper)
            distance = "Invalid limits";
        else if (!double.IsFinite(value))
            distance = "Result unavailable";
        else if (value < lower)
            distance = $"{Gap(lower!.Value - value)} below lower limit";
        else if (value > upper)
            distance = $"{Gap(value - upper!.Value)} above upper limit";
        else if (value == lower && value == upper)
            distance = "At both limits";
        else if (value == lower)
            distance = "At lower limit";
        else if (value == upper)
            distance = "At upper limit";
        else if (lower is { } lowerBound && (upper is null || value - lowerBound <= upper.Value - value))
            distance = $"Margin {Gap(value - lowerBound)} to lower limit";
        else
            distance = $"Margin {Gap(upper!.Value - value)} to upper limit";

        return new(Reading(lower), Reading(value), Reading(upper), distance);
    }

    private static string Format(double number, string format) => number.ToString(format, CultureInfo.InvariantCulture);
}
