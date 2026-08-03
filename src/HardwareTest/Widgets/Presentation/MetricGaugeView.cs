using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using HardwareTest.Features.Presentation;

namespace HardwareTest.Widgets.Presentation;

/// Compact KPI / passband readout (Avalonia-only; no OpenTAP types).
public sealed class MetricGaugeView : UserControl
{
    private readonly TextBlock _key = new() { FontSize = 11, Opacity = 0.7 };
    private readonly TextBlock _value = new() { FontSize = 22, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _limits = new() { FontSize = 11, Opacity = 0.75 };
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
                Children = { _key, _value, _limits, _bandTrack },
            },
        };

        DataContextChanged += (_, _) => BindFromContext();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            BindFromContext();
        }
    }

    private void BindFromContext()
    {
        if (DataContext is not PresentationTileViewModel tile)
        {
            return;
        }

        _key.Text = tile.MetricKey;
        _value.Text = tile.ValueText;
        _limits.Text = tile.LimitsText;
        _limits.IsVisible = !string.IsNullOrWhiteSpace(tile.LimitsText);
        _bandTrack.IsVisible = tile.ShowBand;
        UpdateBand(tile);

        tile.PropertyChanged -= OnTilePropertyChanged;
        tile.PropertyChanged += OnTilePropertyChanged;
    }

    private void OnTilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not PresentationTileViewModel tile)
        {
            return;
        }

        _value.Text = tile.ValueText;
        _limits.Text = tile.LimitsText;
        _limits.IsVisible = !string.IsNullOrWhiteSpace(tile.LimitsText);
        _bandTrack.IsVisible = tile.ShowBand;
        UpdateBand(tile);
    }

    private void UpdateBand(PresentationTileViewModel tile)
    {
        if (!tile.ShowBand)
        {
            return;
        }

        var low = tile.LimitLow ?? tile.Value - Math.Abs(tile.Value) - 1;
        var high = tile.LimitHigh ?? Math.Max(tile.Value, low + 1);
        if (high <= low)
        {
            high = low + 1;
        }

        var span = high - low;
        var ratio = Math.Clamp((tile.Value - low) / span, 0, 1);
        _bandFill.Width = Math.Max(4, ratio * Math.Max(_bandTrack.Bounds.Width, 120));

        _bandMarkers.Children.Clear();
        if (tile.LimitLow is { } ll)
        {
            var x = Math.Clamp((ll - low) / span, 0, 1) * Math.Max(_bandTrack.Bounds.Width, 120);
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
            var x = Math.Clamp((lh - low) / span, 0, 1) * Math.Max(_bandTrack.Bounds.Width, 120);
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
