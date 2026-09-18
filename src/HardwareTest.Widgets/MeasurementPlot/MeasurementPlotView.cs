using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ScottPlot;
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
    private (double ElapsedSec, string Label)[] _events = [];
    private (double T0, double T1)[] _oobSpans = [];
    private double? _cursorX;
    private Point? _pressPosition;

    public MeasurementPlotView()
    {
        Content = _plot;
        ApplyThemeAndLabels();
        _plot.PointerPressed += OnPointerPressed;
        _plot.PointerReleased += OnPointerReleased;
    }

    /// Fired when the operator places, moves, or clears the readout cursor.
    public event EventHandler<PlotCursorChangedEventArgs>? CursorChanged;

    /// Elapsed-seconds (or sample index) of the current readout line, if any.
    internal double? CursorX => _cursorX;

    /// Whether the next render auto-scales to the visible window.
    internal bool FollowLive => _followLive;

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

    /// Vertical event ticks on the elapsed axis (applied on the next render).
    public void SetEvents(IReadOnlyList<(double ElapsedSec, string Label)> events)
        => _events = events.ToArray();

    /// Contiguous out-of-band spans on the elapsed axis (applied on the next render).
    public void SetOutOfBandSpans(IReadOnlyList<(double T0, double T1)> spans)
        => _oobSpans = spans.ToArray();

    /// Redraws the current series so stored event ticks and OOB spans appear without a new sample.
    public void RefreshOverlays() => Render(force: true);

    /// Points drawn by the last completed render. Unchanged when a refresh is throttled.
    internal int LastRenderedPointCount { get; private set; }

    /// Restores follow-live autoscale on the current buffer and drops the readout cursor.
    public void ResetView()
    {
        _followLive = true;
        SetCursor(null, announce: false);
        Render(force: true);
    }

    /// Places or moves the vertical readout line. Null clears a placed line; it does not resume follow-live when none is placed.
    public void SetCursor(double? xElapsedOrIndex, bool announce = true)
    {
        if (xElapsedOrIndex is null)
        {
            if (_cursorX is null)
            {
                return;
            }

            _cursorX = null;
            _followLive = true;
            Render(force: true);
            if (announce)
            {
                CursorChanged?.Invoke(this, new PlotCursorChangedEventArgs());
            }

            return;
        }

        PlaceCursor(xElapsedOrIndex.Value, announce);
    }

    /// Removes the readout line and notifies listeners.
    public void ClearCursor() => SetCursor(null);

    /// Updates a time-based scatter from elapsed-second Xs and values.
    public void UpdateTimeSeries(double[] xs, double[] ys, int count = -1, bool followLive = true, bool force = false)
    {
        _useTimeAxis = true;
        _xLabel = "Time (s)";
        var length = count < 0 ? Math.Min(xs.Length, ys.Length) : Math.Clamp(count, 0, Math.Min(xs.Length, ys.Length));
        EnsureCopy(ref _xs, xs, length);
        EnsureCopy(ref _ys, ys, length);
        SyncCursorAfterBufferChange(followLive);
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
        SyncCursorAfterBufferChange(_followLive);
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

        AddOutOfBandSpans();
        AddEventTicks();
        AddCursorLine();
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

    private void AddEventTicks()
    {
        foreach (var (elapsed, label) in _events)
        {
            var line = _plot.Plot.Add.VerticalLine(elapsed);
            line.LineWidth = 1.25f;
            line.Color = PlotTheme.EventColor;
            if (!string.IsNullOrWhiteSpace(label))
            {
                line.LegendText = label;
            }
        }
    }

    private void AddCursorLine()
    {
        if (_cursorX is not { } x)
        {
            return;
        }

        var line = _plot.Plot.Add.VerticalLine(x);
        line.LineWidth = 1.75f;
        line.Color = PlotTheme.CursorColor;
        line.LegendText = "Readout";
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_plot).Properties.IsLeftButtonPressed)
        {
            _pressPosition = null;
            return;
        }

        _pressPosition = e.GetPosition(_plot);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressPosition is not { } start || e.InitialPressMouseButton != MouseButton.Left)
        {
            _pressPosition = null;
            return;
        }

        var end = e.GetPosition(_plot);
        _pressPosition = null;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (!PlotCursorReadout.IsTap(dx, dy))
        {
            return;
        }

        if (LastRenderedPointCount <= 0)
        {
            return;
        }

        var coords = _plot.Plot.GetCoordinates(
            (float)end.X,
            (float)end.Y,
            _plot.Plot.Axes.Bottom,
            _plot.Plot.Axes.Left);
        if (double.IsNaN(coords.X) || double.IsNaN(coords.Y))
        {
            return;
        }

        PlaceCursor(coords.X, announce: true);
    }

    private void SyncCursorAfterBufferChange(bool requestedFollowLive)
    {
        if (_cursorX is not { } x)
        {
            _followLive = requestedFollowLive;
            return;
        }

        var xs = _useTimeAxis ? _xs : [];
        var ys = _useTimeAxis ? _ys : _signalBuffer;
        if (!PlotCursorReadout.TryNearestSample(xs, ys, ys.Length, x, out _, out var sampleX, out _))
        {
            _cursorX = null;
            _followLive = true;
            return;
        }

        _cursorX = sampleX;
        _followLive = false;
    }

    private void PlaceCursor(double x, bool announce)
    {
        var xs = _useTimeAxis ? _xs : [];
        var ys = _useTimeAxis ? _ys : _signalBuffer;
        if (!PlotCursorReadout.TryNearestSample(xs, ys, ys.Length, x, out var index, out var sampleX, out var sampleY))
        {
            return;
        }

        _cursorX = sampleX;
        _followLive = false;
        Render(force: true);
        if (announce)
        {
            CursorChanged?.Invoke(
                this,
                new PlotCursorChangedEventArgs
                {
                    X = sampleX,
                    Y = sampleY,
                    SampleIndex = index,
                });
        }
    }

    private void AddOutOfBandSpans()
    {
        if (!TryVisibleYRange(out var y1, out var y2))
        {
            return;
        }

        foreach (var (t0, t1) in _oobSpans)
        {
            var left = Math.Min(t0, t1);
            var right = Math.Max(t0, t1);
            if (right <= left)
            {
                right = left + 0.001;
            }

            var fill = _plot.Plot.Add.Rectangle(left, right, y1, y2);
            fill.FillColor = PlotTheme.OutOfBandFillColor;
            fill.LineWidth = 0;
        }
    }

    private bool TryVisibleYRange(out double y1, out double y2)
    {
        if (_ys.Length == 0 && _signalBuffer.Length == 0)
        {
            y1 = 0;
            y2 = 1;
            return _oobSpans.Length > 0;
        }

        var source = _ys.Length > 0 ? _ys : _signalBuffer;
        y1 = source[0];
        y2 = source[0];
        foreach (var v in source)
        {
            y1 = Math.Min(y1, v);
            y2 = Math.Max(y2, v);
        }

        if (_limitLow is { } lo)
        {
            y1 = Math.Min(y1, lo);
        }

        if (_limitHigh is { } hi)
        {
            y2 = Math.Max(y2, hi);
        }

        if (y2 <= y1)
        {
            y2 = y1 + 1;
        }

        return true;
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
