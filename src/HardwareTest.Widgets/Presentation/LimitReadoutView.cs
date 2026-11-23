using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HardwareTest.Widgets.Presentation;

/// Keeps the result between its lower and upper limits, including in compact cards.
public sealed class LimitReadoutView : UserControl
{
    public static readonly StyledProperty<LimitReadout?> ReadingProperty =
        AvaloniaProperty.Register<LimitReadoutView, LimitReadout?>(nameof(Reading));

    private readonly TextBlock _lower = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _value = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _upper = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _distance = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };

    public LimitReadoutView()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        AddColumn(0, "Low", _lower);
        AddColumn(1, "Value", _value);
        AddColumn(2, "High", _upper);
        Content = new StackPanel { Spacing = 3, Children = { columns, _distance } };
        IsVisible = false;

        void AddColumn(int index, string label, TextBlock value)
        {
            var column = new StackPanel
            {
                Spacing = 1,
                Children = { new TextBlock { Text = label, FontSize = 11, Opacity = 0.8 }, value },
            };
            Grid.SetColumn(column, index);
            columns.Children.Add(column);
        }
    }

    public LimitReadout? Reading
    {
        get => GetValue(ReadingProperty);
        set => SetValue(ReadingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ReadingProperty)
        {
            return;
        }

        IsVisible = Reading is not null;
        if (Reading is not { } reading)
        {
            return;
        }

        SetReading(_lower, "Lower limit", reading.Lower);
        SetReading(_value, "Result value", reading.Value);
        SetReading(_upper, "Upper limit", reading.Upper);
        _distance.Text = reading.Distance;
    }

    private static void SetReading(TextBlock block, string label, string value)
    {
        block.Text = value;
        AutomationProperties.SetName(block, $"{label} {value}");
    }
}
