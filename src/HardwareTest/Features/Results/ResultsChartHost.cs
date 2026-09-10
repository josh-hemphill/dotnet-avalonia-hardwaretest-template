using Avalonia.Controls;
using Avalonia.Threading;
using HardwareTest.Features.Presentation;
using HardwareTest.Widgets.MeasurementPlot;

namespace HardwareTest.Features.Results;

/// Hosts a MeasurementPlotView bound to a timeseries PresentationTileViewModel.
public sealed class ResultsChartHost : UserControl
{
    private readonly MeasurementPlotView _plot = new() { MinHeight = 240 };

    public ResultsChartHost()
    {
        Content = _plot;
        DataContextChanged += (_, _) => Refresh();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
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
            _plot.SetEvents(tile.TimingMarks.Select(e => (e.ElapsedMs / 1000.0, SeriesTimingChrome.FormatEventLabel(e))).ToList());
            _plot.SetOutOfBandSpans(tile.OutOfBandSpans);
            if (tile.Xs.Length == tile.YsLength && tile.YsLength > 0)
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
