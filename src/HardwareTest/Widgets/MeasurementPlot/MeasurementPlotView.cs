using System;
using Avalonia;
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
    private double[] _xs = [];
    private double[] _ys = [];
    private string _title = "Live measurements";
    private string _yLabel = "Value";
    private string _legendText = "Channel";
    private string _xLabel = "Sample";
    private double? _limitLow;
    private double? _limitHigh;
    private bool _themeHooked;
    private bool _useTimeAxis;
    private bool _followLive = true;

    public MeasurementPlotView()
    {
        Content = _plot;
        ApplyThemeAndLabels();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookTheme();
        ApplyThemeAndLabels();
        _plot.Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnhookTheme();
        base.OnDetachedFromVisualTree(e);
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

        ApplyThemeAndLabels();
    }

    /// Optional horizontal limit lines / passband overlay. Applied on the next render.
    public void SetLimits(double? limitLow, double? limitHigh)
    {
        _limitLow = limitLow;
        _limitHigh = limitHigh;
    }

    /// When true, each data update auto-scales axes to the visible window.
    public void SetFollowLive(bool followLive) => _followLive = followLive;

    /// Points drawn by the last completed render. Unchanged when a refresh is throttled.
    internal int LastRenderedPointCount { get; private set; }

    /// Restores follow-live autoscale on the current buffer.
    public void ResetView()
    {
        _followLive = true;
        Render(force: true);
    }

    /// Updates a time-based scatter from elapsed-second Xs and values.
    public void UpdateTimeSeries(double[] xs, double[] ys, int count = -1, bool followLive = true, bool force = false)
    {
        _useTimeAxis = true;
        _xLabel = "Time (s)";
        _followLive = followLive;
        var length = count < 0 ? Math.Min(xs.Length, ys.Length) : Math.Clamp(count, 0, Math.Min(xs.Length, ys.Length));
        EnsureCopy(ref _xs, xs, length);
        EnsureCopy(ref _ys, ys, length);
        Render(force);
    }

    /// Updates the Signal plot from a reusable buffer (UI thread; throttled).
    public void UpdateData(double[] ys, int count = -1, bool force = false)
    {
        _useTimeAxis = false;
        _xLabel = "Sample";
        var length = count < 0 ? ys.Length : Math.Clamp(count, 0, ys.Length);
        if (_signalBuffer.Length != length)
        {
            _signalBuffer = new double[length];
        }

        if (length > 0)
        {
            Array.Copy(ys, 0, _signalBuffer, 0, length);
        }

        _ys = _signalBuffer;
        Render(force);
    }

    private void Render(bool force)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Render(force));
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && now - _lastRefresh < _minInterval)
        {
            return;
        }

        _lastRefresh = now;
        LastRenderedPointCount = _useTimeAxis ? _ys.Length : _signalBuffer.Length;
        _plot.Plot.Clear();
        ApplyThemeAndLabels();
        if (_useTimeAxis)
        {
            DrawScatter();
        }
        else if (_signalBuffer.Length > 0)
        {
            var signal = _plot.Plot.Add.Signal(_signalBuffer);
            signal.LegendText = _legendText;
            signal.Color = PlotTheme.SeriesColor;
            signal.LineWidth = 2;
        }

        AddLimitOverlay();
        if (_followLive)
        {
            _plot.Plot.Axes.AutoScale();
        }

        _plot.Refresh();
    }

    private void DrawScatter()
    {
        if (_ys.Length == 0)
        {
            return;
        }

        var scatter = _plot.Plot.Add.Scatter(_xs, _ys);
        scatter.LegendText = _legendText;
        scatter.Color = PlotTheme.SeriesColor;
        scatter.LineWidth = 2;
        scatter.MarkerSize = 0;
    }

    private void AddLimitOverlay()
    {
        if (_limitLow is { } low && _limitHigh is { } high && TryVisibleXRange(out var x1, out var x2))
        {
            var fill = _plot.Plot.Add.Rectangle(x1, x2, low, high);
            fill.FillColor = PlotTheme.LimitFillColor;
            fill.LineWidth = 0;
        }

        if (_limitLow is { } lo)
        {
            var line = _plot.Plot.Add.HorizontalLine(lo);
            line.LegendText = "LimitLow";
            line.LineWidth = 1.5f;
            line.Color = PlotTheme.LimitColor;
        }

        if (_limitHigh is { } hi)
        {
            var line = _plot.Plot.Add.HorizontalLine(hi);
            line.LegendText = "LimitHigh";
            line.LineWidth = 1.5f;
            line.Color = PlotTheme.LimitColor;
        }
    }

    private bool TryVisibleXRange(out double x1, out double x2)
    {
        if (_useTimeAxis && _xs.Length > 0)
        {
            x1 = _xs[0];
            x2 = _xs[^1];
            if (x2 <= x1)
            {
                x2 = x1 + 1;
            }

            return true;
        }

        if (!_useTimeAxis && _signalBuffer.Length > 1)
        {
            x1 = 0;
            x2 = _signalBuffer.Length - 1;
            return true;
        }

        x1 = 0;
        x2 = 1;
        return _limitLow is not null && _limitHigh is not null;
    }

    private static void EnsureCopy(ref double[] target, double[] source, int length)
    {
        if (target.Length != length)
        {
            target = new double[length];
        }

        if (length > 0)
        {
            Array.Copy(source, 0, target, 0, length);
        }
    }

    private void ApplyThemeAndLabels()
    {
        PlotTheme.Apply(_plot.Plot);
        _plot.Plot.Title(_title);
        _plot.Plot.XLabel(_xLabel);
        _plot.Plot.YLabel(_yLabel);
    }

    private void HookTheme()
    {
        if (_themeHooked || Application.Current is null)
        {
            return;
        }

        Application.Current.ActualThemeVariantChanged += OnThemeVariantChanged;
        _themeHooked = true;
    }

    private void UnhookTheme()
    {
        if (!_themeHooked || Application.Current is null)
        {
            return;
        }

        Application.Current.ActualThemeVariantChanged -= OnThemeVariantChanged;
        _themeHooked = false;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        void RefreshTheme() => Render(force: true);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshTheme);
            return;
        }

        RefreshTheme();
    }
}
