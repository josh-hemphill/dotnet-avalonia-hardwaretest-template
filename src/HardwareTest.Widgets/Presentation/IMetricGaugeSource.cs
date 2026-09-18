using System.ComponentModel;

namespace HardwareTest.Widgets.Presentation;

/// Binding surface for MetricGaugeView. Operator tiles and authoring preview both implement this.
public interface IMetricGaugeSource : INotifyPropertyChanged
{
    string MetricKey { get; }
    string ValueText { get; }
    string LimitsText { get; }
    bool ShowBand { get; }
    double Value { get; }
    double? LimitLow { get; }
    double? LimitHigh { get; }
}
