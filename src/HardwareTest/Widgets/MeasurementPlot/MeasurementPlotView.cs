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
    private double[] _signalBuffer = [];

    public MeasurementPlotView()
    {
        Content = _plot;
        _plot.Plot.Title("Live measurements");
        _plot.Plot.XLabel("Sample");
        _plot.Plot.YLabel("Value");
    }

    /// Updates the Signal plot from a reusable buffer (UI thread; throttled).
    public void UpdateData(double[] ys, int count = -1, bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateData(ys, count, force));
            return;
        }

        var length = count < 0 ? ys.Length : Math.Clamp(count, 0, ys.Length);
        var now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < _minInterval)
        {
            return;
        }

        _lastRefresh = now;
        _plot.Plot.Clear();
        if (length > 0)
        {
            // Copy into an exact-length buffer so Signal does not plot unused tail zeros.
            // Grows at most once up to the live window size (no per-flush List/ToArray on the VM).
            if (_signalBuffer.Length != length)
            {
                _signalBuffer = new double[length];
            }

            Array.Copy(ys, 0, _signalBuffer, 0, length);
            var signal = _plot.Plot.Add.Signal(_signalBuffer);
            signal.LegendText = "Channel";
            _plot.Plot.Axes.AutoScale();
        }

        _plot.Refresh();
    }
}
