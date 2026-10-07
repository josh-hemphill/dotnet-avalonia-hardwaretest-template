using Avalonia.Controls;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;

namespace HardwareTest.Authoring;

/// Renders the shared operator widgets for one board calculation.
public sealed class BoardPreviewTileView : UserControl
{
    private readonly MetricGaugeView _gauge = new() { Width = 200 };
    private readonly MeasurementPlotView _plot = new() { MinHeight = 180, Height = 180 };
    private readonly TimingStripView _strip = new();

    public BoardPreviewTileView(BoardPreviewTile tile)
    {
        // Let the board scroll over a chart until the engineer explicitly enables plot input.
        _plot.IsHitTestVisible = false;
        var chartInput = new CheckBox { Content = "Interact with chart", IsVisible = tile.Chrome.IsChart };
        Avalonia.Automation.AutomationProperties.SetName(chartInput, $"Interact with chart {tile.Preview.ChannelKey}");
        chartInput.IsCheckedChanged += (_, _) => _plot.IsHitTestVisible = chartInput.IsChecked == true;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"{tile.Title} · {tile.Preview.ChannelKey}", FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = tile.Scope, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = tile.Preview.Note, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = tile.Preview.CannedSamples.Count == 0 ? "Unavailable" : $"{tile.Chrome.ValueText} · {tile.Preview.CannedSamples.Count} samples · {(tile.Chrome.UsesTimeAxis ? "elapsed time" : "sample index")} · {(tile.Preview.Passed is true ? "Pass" : tile.Preview.Passed is false ? "Fail" : "no criterion verdict")}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (tile.Preview.Threshold is { } threshold)
            panel.Children.Add(new TextBlock { Text = $"Criterion: ≥ {threshold.ToString("G6", System.Globalization.CultureInfo.InvariantCulture)} {tile.Preview.YUnit}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(_gauge);
        panel.Children.Add(chartInput);
        panel.Children.Add(_plot);
        panel.Children.Add(_strip);
        Content = new Border { Padding = new Avalonia.Thickness(8), Child = panel };
        Avalonia.Automation.AutomationProperties.SetName(this, $"Board tile {tile.Preview.ChannelKey}");
        Apply(tile.Chrome);
    }

    private void Apply(AuthoringPreviewChrome chrome)
    {
        _gauge.IsVisible = chrome.IsGauge;
        _plot.IsVisible = chrome.IsChart;
        _strip.IsVisible = chrome.IsStrip;
        if (chrome.IsGauge)
        {
            _gauge.DataContext = new MetricGaugeModel
            {
                MetricKey = chrome.MetricKey,
                ValueText = chrome.ValueText,
                LimitsText = chrome.LimitsText,
                ShowBand = chrome.ShowBand,
                Value = chrome.Value,
                LimitLow = chrome.LimitLow,
                LimitHigh = chrome.LimitHigh,
            };
        }

        if (chrome.IsChart)
        {
            var unit = string.IsNullOrWhiteSpace(chrome.YUnit) ? "Value" : chrome.YUnit;
            _plot.SetLabels(chrome.MetricKey, unit, chrome.MetricKey);
            _plot.SetLimits(chrome.LimitLow, chrome.LimitHigh);
            var ys = chrome.Ys.ToArray();
            var plan = AuthoringPreviewChromeBuilder.ChartPlan(chrome);
            if (plan.DrawTimeAxis)
            {
                _plot.SetEvents(plan.EventTicks.ToArray());
                _plot.SetOutOfBandSpans(plan.Spans.ToArray());
                _plot.UpdateTimeSeries(chrome.Xs.ToArray(), ys, ys.Length, followLive: true, force: true);
            }
            else
            {
                _plot.SetEvents([]);
                _plot.SetOutOfBandSpans([]);
                _plot.UpdateData(ys, ys.Length, force: true);
            }
        }

        if (chrome.IsStrip)
        {
            _strip.Events = chrome.Events;
            _strip.Spans = chrome.UsesTimeAxis ? chrome.Spans : [];
            _strip.DurationSec = chrome.DurationSec;
        }
    }

    internal void SetChartViewportHeight(double viewportHeight)
    {
        if (viewportHeight <= 0 || !double.IsFinite(viewportHeight)) return;
        // Reserve the measured title, provenance, criterion and chart-input controls too.
        // A tile revealed as a whole should not clip the bottom of its chart.
        var chrome = Bounds.Height > 0 && _plot.Bounds.Height > 0
            ? Math.Max(0, Bounds.Height - _plot.Bounds.Height) : 160;
        var height = Math.Clamp(viewportHeight - chrome - 16, 64, 180);
        if (Math.Abs(_plot.Height - height) <= 0.5) return;
        _plot.MinHeight = height;
        _plot.Height = height;
    }

}
