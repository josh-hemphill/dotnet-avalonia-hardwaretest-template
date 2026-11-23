using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HardwareTest.Widgets.Presentation;

/// Compact KPI / passband readout (Avalonia-only; no OpenTAP types).
public sealed class MetricGaugeView : UserControl
{
    private readonly TextBlock _key = new() { FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _value = new() { FontSize = 22, FontWeight = FontWeight.SemiBold };
    private readonly LimitReadoutView _reading = new();
    private readonly TextBlock _limits = new() { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private IMetricGaugeSource? _source;
    private readonly Border _bandTrack = new()
    {
        Height = 8,
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
        ClipToBounds = true,
    };
    private readonly Border _bandFill = new()
    {
        Height = 8,
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180)),
    };
    private readonly Canvas _bandMarkers = new() { Height = 8 };

    public MetricGaugeView()
    {
        _bandTrack.Child = new Panel
        {
            Children = { _bandFill, _bandMarkers },
        };

        Content = new Border
        {
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { _key, _value, _reading, _limits, _bandTrack },
            },
        };

        DataContextChanged += (_, _) => BindFromContext();
        AttachedToVisualTree += (_, _) => BindFromContext();
        DetachedFromVisualTree += (_, _) => Subscribe(null);
        _bandTrack.SizeChanged += (_, _) =>
        {
            if (_source is { } source && _bandTrack.IsVisible)
                UpdateBand(source);
        };
    }

    private void BindFromContext()
    {
        var source = DataContext as IMetricGaugeSource;
        Subscribe(this.IsAttachedToVisualTree() ? source : null);
        Refresh(source);
    }

    private void Subscribe(IMetricGaugeSource? source)
    {
        if (_source is not null) _source.PropertyChanged -= OnTilePropertyChanged;
        _source = source;
        if (_source is not null) _source.PropertyChanged += OnTilePropertyChanged;
    }

    private void OnTilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(_source);

    private void Refresh(IMetricGaugeSource? tile)
    {
        _key.Text = tile?.MetricKey;
        _value.Text = tile?.ValueText;
        _reading.Reading = tile is null ? null : LimitReadout.Create(tile.Value, tile.LimitLow, tile.LimitHigh, tile.Unit);
        _value.IsVisible = _reading.Reading is null;
        _limits.Text = tile?.LimitsText;
        _limits.IsVisible = _reading.Reading is null && !string.IsNullOrWhiteSpace(_limits.Text);
        _bandTrack.IsVisible = tile is { ShowBand: true } && double.IsFinite(tile.Value)
            && (tile.LimitLow is null || double.IsFinite(tile.LimitLow.Value))
            && (tile.LimitHigh is null || double.IsFinite(tile.LimitHigh.Value))
            && !(tile.LimitLow > tile.LimitHigh);
        if (_bandTrack.IsVisible)
            UpdateBand(tile!);
    }

    private void UpdateBand(IMetricGaugeSource tile)
    {
        var width = _bandTrack.Bounds.Width;
        if (width <= 0) return;

        var low = tile.LimitLow ?? tile.Value - Math.Abs(tile.Value) - 1;
        var high = tile.LimitHigh ?? Math.Max(tile.Value, low + 1);
        if (high <= low)
        {
            high = low + 1;
        }

        var span = high - low;
        var ratio = Math.Clamp((tile.Value - low) / span, 0, 1);
        _bandFill.Width = ratio * width;

        _bandMarkers.Children.Clear();
        if (tile.LimitLow is { } ll)
        {
            var x = Math.Clamp((ll - low) / span, 0, 1) * Math.Max(0, width - 2);
            _bandMarkers.Children.Add(new Rectangle
            {
                Width = 2,
                Height = 8,
                Fill = Brushes.OrangeRed,
                [Canvas.LeftProperty] = x,
            });
        }

        if (tile.LimitHigh is { } lh)
        {
            var x = Math.Clamp((lh - low) / span, 0, 1) * Math.Max(0, width - 2);
            _bandMarkers.Children.Add(new Rectangle
            {
                Width = 2,
                Height = 8,
                Fill = Brushes.OrangeRed,
                [Canvas.LeftProperty] = x,
            });
        }
    }
}
