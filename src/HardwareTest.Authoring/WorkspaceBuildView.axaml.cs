using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceBuildView : UserControl
{
    public WorkspaceBuildView() => InitializeComponent();

    private void OnPack(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnPack(sender, e);
}
