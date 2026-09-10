using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using HardwareTest.Features.Presentation;
using HardwareTest.Widgets.MeasurementPlot;

namespace HardwareTest.Features.Results;

/// Hosts a MeasurementPlotView bound to a timeseries PresentationTileViewModel.
public sealed class ResultsChartHost : UserControl
{
    private readonly MeasurementPlotView _plot = new() { MinHeight = 240 };
    private INotifyPropertyChanged? _tileNotify;

    public ResultsChartHost()
    {
        Content = _plot;
        DataContextChanged += (_, _) => HookDataContext();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
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
        void Apply()
        {
            var unit = string.IsNullOrWhiteSpace(tile.Unit) ? "Value" : tile.Unit!;
            _plot.SetLabels(tile.MetricKey, unit, tile.MetricKey);
            _plot.SetLimits(tile.LimitLow, tile.LimitHigh);
            if (tile.UsesTimeAxis)
            {
                _plot.SetEvents(tile.TimingMarks.Select(e => (e.ElapsedMs / 1000.0, SeriesTimingChrome.FormatEventLabel(e))).ToList());
                _plot.SetOutOfBandSpans(tile.OutOfBandSpans);
            }
            else
            {
                _plot.SetEvents([]);
                _plot.SetOutOfBandSpans([]);
            }

            if (tile.UsesTimeAxis && tile.Xs.Length == tile.YsLength && tile.YsLength > 0)
            {
                _plot.UpdateTimeSeries(tile.Xs, tile.Ys, tile.YsLength, followLive: true, force: true);
            }
            else
            {
                _plot.UpdateData(tile.Ys, tile.YsLength, force: true);
            }
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Apply);
            return;
        }

        Apply();
    }
}
