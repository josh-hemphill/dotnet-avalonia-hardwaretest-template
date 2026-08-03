using Avalonia.Controls;
using Avalonia.Input;

namespace HardwareTest.Features.Results;

public partial class ResultsView : UserControl
{
    public ResultsView()
    {
        InitializeComponent();
    }

    private async void OnRunsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ResultsViewModel vm)
        {
            await vm.OpenSelectedRunDefaultReportAsync();
        }
    }
}
