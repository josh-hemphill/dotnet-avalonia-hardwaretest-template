using System.ComponentModel;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HardwareTest.Features.Presentation;
using HardwareTest.Widgets.MeasurementPlot;

namespace HardwareTest.Features.Results;

/// Hosts a MeasurementPlotView bound to a timeseries PresentationTileViewModel.
public sealed class ResultsChartHost : UserControl
{
    private const string TapHint = "Tap the plot to place a readout line.";
    private const string InteractionHint = "Enable interaction to inspect chart values.";

    private readonly MeasurementPlotView _plot = new() { MinHeight = 240, IsHitTestVisible = false };
    private readonly CheckBox _interaction = new() { Content = "Interact with chart" };
    private readonly TextBlock _cursorReadout = new()
    {
        Opacity = 0.85,
        Margin = new Avalonia.Thickness(0, 6, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Text = InteractionHint,
        IsVisible = true,
    };
    private INotifyPropertyChanged? _tileNotify;

    public ResultsChartHost()
    {
        var panel = new DockPanel();
        AutomationProperties.SetName(_interaction, "Interact with saved results chart");
        _interaction.IsCheckedChanged += (_, _) =>
        {
            _plot.IsHitTestVisible = _interaction.IsChecked == true;
            if (!_plot.IsHitTestVisible) _plot.ClearCursor();
            SyncReadout();
        };
        DockPanel.SetDock(_interaction, Dock.Top);
        panel.Children.Add(_interaction);
        DockPanel.SetDock(_cursorReadout, Dock.Bottom);
        panel.Children.Add(_cursorReadout);
        panel.Children.Add(_plot);
        Content = panel;
        DataContextChanged += (_, _) => HookDataContext();
        _plot.CursorChanged += OnCursorChanged;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PresentationTileViewModel { IsChart: true })
        {
            Refresh();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnhookTile();
        base.OnDetachedFromVisualTree(e);
    }

    private void HookDataContext()
    {
        UnhookTile();
        if (DataContext is INotifyPropertyChanged notify)
        {
            _tileNotify = notify;
            notify.PropertyChanged += OnTilePropertyChanged;
        }

        Refresh();
    }

    private void UnhookTile()
    {
        if (_tileNotify is null)
        {
            return;
        }

        _tileNotify.PropertyChanged -= OnTilePropertyChanged;
        _tileNotify = null;
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PresentationTileViewModel.TimingMarks)
            or nameof(PresentationTileViewModel.OutOfBandSpans)
            or nameof(PresentationTileViewModel.Xs)
            or nameof(PresentationTileViewModel.Ys)
            or nameof(PresentationTileViewModel.YsLength)
            or nameof(PresentationTileViewModel.UsesTimeAxis)
            or nameof(PresentationTileViewModel.LimitLow)
            or nameof(PresentationTileViewModel.LimitHigh))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (DataContext is not PresentationTileViewModel tile || !tile.IsChart)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        _cursorReadout.IsVisible = true;
        void Apply()
        {
            var unit = string.IsNullOrWhiteSpace(tile.Unit) ? "Value" : tile.Unit!;
            _plot.SetLabels(tile.MetricKey, unit, tile.MetricKey);
            _plot.SetLimits(tile.LimitLow, tile.LimitHigh);
            var paused = _plot.CursorX is not null;
            var drawTimeAxis = tile.UsesTimeAxis && tile.Xs.Length == tile.YsLength && tile.YsLength > 0;
            if (drawTimeAxis)
            {
                _plot.SetEvents(SeriesTimingChrome.ToPlotTicks(tile.TimingMarks));
                _plot.SetOutOfBandSpans(tile.OutOfBandSpans);
                _plot.UpdateTimeSeries(tile.Xs, tile.Ys, tile.YsLength, followLive: !paused, force: true);
            }
            else
            {
                _plot.SetEvents([]);
                _plot.SetOutOfBandSpans([]);
                _plot.SetFollowLive(!paused);
                _plot.UpdateData(tile.Ys, tile.YsLength, force: true);
            }

            SyncReadout();
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Apply);
            return;
        }

        Apply();
    }

    private void SyncReadout()
    {
        if (_plot.CursorX is not null)
        {
            _plot.SetCursor(_plot.CursorX, announce: true);
            if (_plot.CursorX is not null)
            {
                return;
            }
        }

        _cursorReadout.Text = _interaction.IsChecked == true ? TapHint : InteractionHint;
        _cursorReadout.IsVisible = true;
    }

    private void OnCursorChanged(object? sender, PlotCursorChangedEventArgs e)
    {
        if (e.X is not { } x || e.Y is not { } y)
        {
            _cursorReadout.Text = _interaction.IsChecked == true ? TapHint : InteractionHint;
            _cursorReadout.IsVisible = true;
            return;
        }

        var tile = DataContext as PresentationTileViewModel;
        var unit = tile is not null && !string.IsNullOrWhiteSpace(tile.Unit) ? tile.Unit : null;
        var value = PlotCursorReadout.FormatValue(y, unit);
        var band = tile is null
            ? string.Empty
            : PlotCursorReadout.FormatBand(y, tile.LimitLow, tile.LimitHigh, unit);
        var at = tile is { UsesTimeAxis: true }
            ? SeriesTimingChrome.FormatElapsed(x * 1000.0)
            : $"sample {(e.SampleIndex ?? (int)Math.Round(x, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)}";
        _cursorReadout.Text = string.IsNullOrWhiteSpace(band)
            ? $"Readout: {value} at {at} — tap to move."
            : $"Readout: {value} at {at}. {band} — tap to move.";
        _cursorReadout.IsVisible = true;
    }
}
