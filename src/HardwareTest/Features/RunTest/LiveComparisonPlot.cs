using Avalonia;
using Avalonia.Controls;
using HardwareTest.Widgets.MeasurementPlot;

namespace HardwareTest.Features.RunTest;

public sealed class LiveComparisonPlot : ContentControl
{
    public static readonly StyledProperty<LiveSeriesSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<LiveComparisonPlot, LiveSeriesSnapshot?>(nameof(Snapshot));

    private readonly MeasurementPlotView _plot = new() { MinHeight = 190, IsHitTestVisible = false };

    public LiveComparisonPlot() => Content = _plot;

    public LiveSeriesSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SnapshotProperty && Snapshot is { } sample)
        {
            _plot.SetLabels(sample.Key.DisplayName, sample.Unit ?? "Value", sample.Key.MetricKey);
            _plot.SetLimits(sample.LimitLow, sample.LimitHigh);
            _plot.UpdateTimeSeries(sample.Xs, sample.Ys, sample.Length);
        }
    }
}
