using System.Globalization;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Presentation;

public enum PresentationTileKind
{
    Timeseries,
    Scalar,
    Passband,
    Text,
}

/// Role-mapped tile for Run gauges and Results charts.
public partial class PresentationTileViewModel : ReactiveObject
{
    public PresentationTileViewModel(
        string metricKey,
        PresentationTileKind kind,
        string? displayRole,
        string? unit,
        string? stepPath)
    {
        MetricKey = metricKey;
        Kind = kind;
        DisplayRole = displayRole;
        Unit = unit;
        StepPath = stepPath ?? string.Empty;
    }

    public string MetricKey { get; }
    public PresentationTileKind Kind { get; }
    public string? DisplayRole { get; }
    public string? Unit { get; }
    public string StepPath { get; set; }

    [Reactive] private double _value;
    [Reactive] private double? _limitLow;
    [Reactive] private double? _limitHigh;
    [Reactive] private double[] _ys = [];
    [Reactive] private int _ysLength;
    [Reactive] private string _valueText = string.Empty;
    [Reactive] private string _limitsText = string.Empty;
    [Reactive] private bool _showBand;

    public bool IsGauge => Kind is PresentationTileKind.Scalar or PresentationTileKind.Passband;
    public bool IsChart => Kind == PresentationTileKind.Timeseries;

    /// Applies a live or stored value and optional limits.
    public void Apply(double value, double? limitLow, double? limitHigh)
    {
        Value = value;
        LimitLow = limitLow;
        LimitHigh = limitHigh;
        ValueText = FormatValue(value, Unit);
        ShowBand = Kind == PresentationTileKind.Passband && (limitLow is not null || limitHigh is not null);
        LimitsText = FormatLimits(limitLow, limitHigh, Unit);
    }

    /// Replaces the timeseries Y buffer for Results charts.
    public void SetSeries(double[] ys)
    {
        Ys = ys;
        YsLength = ys.Length;
        if (ys.Length > 0)
        {
            Apply(ys[^1], LimitLow, LimitHigh);
        }
    }

    private static string FormatValue(double value, string? unit)
    {
        var v = value.ToString("G6", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? v : $"{v} {unit}";
    }

    private static string FormatLimits(double? low, double? high, string? unit)
    {
        if (low is null && high is null)
        {
            return string.Empty;
        }

        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        if (low is not null && high is not null)
        {
            return $"[{low.Value.ToString("G6", CultureInfo.InvariantCulture)} … {high.Value.ToString("G6", CultureInfo.InvariantCulture)}]{suffix}";
        }

        if (low is not null)
        {
            return $"≥ {low.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
        }

        return $"≤ {high!.Value.ToString("G6", CultureInfo.InvariantCulture)}{suffix}";
    }
}
