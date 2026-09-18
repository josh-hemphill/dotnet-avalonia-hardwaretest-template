using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;

namespace HardwareTest.Authoring;

/// Hosts the operator MetricGaugeView / plot / timing strip for the authoring preview column.
public sealed class OperatorPreviewPane : UserControl
{
    private readonly TextBlock _kind = new() { Opacity = 0.85 };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly MetricGaugeView _gauge = new() { Width = 200 };
    private readonly MeasurementPlotView _plot = new() { MinHeight = 180, Height = 180 };
    private readonly TimingStripView _strip = new();
    private AuthoringWorkspaceViewModel? _session;

    public OperatorPreviewPane()
    {
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _kind,
                _note,
                _gauge,
                _plot,
                _strip,
            },
        };
        AutomationProperties.SetName(this, "Operator preview chrome");
        DataContextChanged += (_, _) => HookSession();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookSession();
        Apply();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnhookSession();
        base.OnDetachedFromVisualTree(e);
    }

    private void HookSession()
    {
        UnhookSession();
        if (DataContext is AuthoringWorkspaceViewModel session)
        {
            _session = session;
            session.PropertyChanged += OnSessionChanged;
        }

        Apply();
    }

    private void UnhookSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged -= OnSessionChanged;
        _session = null;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.Preview)
            or nameof(AuthoringWorkspaceViewModel.PreviewChrome)
            or nameof(AuthoringWorkspaceViewModel.PreviewKind)
            or nameof(AuthoringWorkspaceViewModel.PreviewNote)
            or nameof(AuthoringWorkspaceViewModel.SelectedDatasetIndex)
            or nameof(AuthoringWorkspaceViewModel.SelectedSequenceIndex)
            or nameof(AuthoringWorkspaceViewModel.SelectedProgram))
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_session is null)
        {
            ShowEmpty();
            return;
        }

        var chrome = _session.PreviewChrome;
        _kind.Text = $"Tile: {_session.PreviewKind}";
        _note.Text = _session.PreviewNote;
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

    private void ShowEmpty()
    {
        _kind.Text = "Tile: Text";
        _note.Text = string.Empty;
        _gauge.IsVisible = false;
        _plot.IsVisible = false;
        _strip.IsVisible = false;
    }
}
