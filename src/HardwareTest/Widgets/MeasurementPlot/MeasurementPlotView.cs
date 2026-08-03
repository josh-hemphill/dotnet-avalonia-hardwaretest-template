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
    private string _title = "Live measurements";
    private string _yLabel = "Value";
    private string _legendText = "Channel";
    private double? _limitLow;
    private double? _limitHigh;

    public MeasurementPlotView()
    {
        Content = _plot;
        ApplyAxisLabels();
    }

    /// Sets plot chrome (title, axis, legend) without requiring a data refresh.
    public void SetLabels(string? title = null, string? yLabel = null, string? legendText = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            _title = title;
        }

        if (!string.IsNullOrWhiteSpace(yLabel))
        {
            _yLabel = yLabel;
        }

        if (!string.IsNullOrWhiteSpace(legendText))
        {
            _legendText = legendText;
        }

        ApplyAxisLabels();
    }

    /// Optional horizontal limit lines for passband overlays.
    public void SetLimits(double? limitLow, double? limitHigh)
    {
        _limitLow = limitLow;
        _limitHigh = limitHigh;
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
        ApplyAxisLabels();
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
            signal.LegendText = _legendText;
            _plot.Plot.Axes.AutoScale();
            AddLimitLines();
        }

        _plot.Refresh();
    }

    private void ApplyAxisLabels()
    {
        _plot.Plot.Title(_title);
        _plot.Plot.XLabel("Sample");
        _plot.Plot.YLabel(_yLabel);
    }

    private void AddLimitLines()
    {
        if (_limitLow is { } low)
        {
            var line = _plot.Plot.Add.HorizontalLine(low);
            line.LegendText = "LimitLow";
            line.LineWidth = 1;
        }

        if (_limitHigh is { } high)
        {
            var line = _plot.Plot.Add.HorizontalLine(high);
            line.LegendText = "LimitHigh";
            line.LineWidth = 1;
        }
    }
}
