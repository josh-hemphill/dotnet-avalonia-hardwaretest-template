using System.ComponentModel;

namespace HardwareTest.Widgets.Presentation;

/// Snapshot gauge source for authoring preview (no ReactiveUI).
public sealed class MetricGaugeModel : IMetricGaugeSource
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public required string MetricKey { get; init; }
    public required string ValueText { get; init; }
    public required string LimitsText { get; init; }
    public bool ShowBand { get; init; }
    public double Value { get; init; }
    public double? LimitLow { get; init; }
    public double? LimitHigh { get; init; }
}
