using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceIssuesView : UserControl
{
    public WorkspaceIssuesView() => InitializeComponent();

    private void OnOpenFindingProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenFindingProgram(sender, e);
}
