using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ScottPlot.Avalonia;

namespace HardwareTest.Widgets.MeasurementPlot;

public sealed class MeasurementPlotView : UserControl
{
    private readonly AvaPlot _plot = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(50);

    public MeasurementPlotView()
    {
        Content = _plot;
        _plot.Plot.Title("Live measurements");
        _plot.Plot.XLabel("Sample");
        _plot.Plot.YLabel("Value");
    }

    /// Updates the Signal plot with the latest Y values (UI thread; throttled).
    public void UpdateData(double[] ys)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateData(ys));
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastRefresh < _minInterval)
        {
            return;
        }

        _lastRefresh = now;
        _plot.Plot.Clear();
        if (ys is { Length: > 0 })
        {
            // Signal plot for evenly spaced acquisition samples (prefer over Scatter).
            var signal = _plot.Plot.Add.Signal(ys);
            signal.LegendText = "Channel";
            _plot.Plot.Axes.AutoScale();
        }

        _plot.Refresh();
    }
}
