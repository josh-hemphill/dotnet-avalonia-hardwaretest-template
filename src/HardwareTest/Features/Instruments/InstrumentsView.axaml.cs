using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HardwareTest.Features.Instruments;

public partial class InstrumentsView : UserControl
{
    private bool? _stacked;

    public InstrumentsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeWorkflow();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == FontSizeProperty) ArrangeWorkflow();
        };
        AttachedToVisualTree += (_, _) => ArrangeWorkflow();
    }

    private void ArrangeWorkflow()
    {
        var stacked = Bounds.Width < 920 || FontSize > 16;
        if (_stacked == stacked) return;
        _stacked = stacked;
        var grid = this.FindControl<Grid>("BindingWorkflowGrid")!;
        var resources = this.FindControl<Border>("ResourcePane")!;
        grid.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,*");
        grid.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
        Grid.SetColumn(resources, stacked ? 0 : 1);
        Grid.SetRow(resources, stacked ? 1 : 0);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
