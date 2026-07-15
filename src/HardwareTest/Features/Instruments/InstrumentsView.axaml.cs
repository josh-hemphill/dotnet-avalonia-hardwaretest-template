using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HardwareTest.Features.Instruments;

public partial class InstrumentsView : UserControl
{
    public InstrumentsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
