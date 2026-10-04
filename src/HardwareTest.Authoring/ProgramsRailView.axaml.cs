using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class ProgramsRailView : UserControl
{
    public ProgramsRailView() => InitializeComponent();

    private void OnCreateProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnCreateProgram(sender, e);

    private void OnRemoveProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveProgram(sender, e);

    private void OnOpenLastWorkspace(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenLastWorkspace(sender, e);

    private void OnProgramsKeyDown(object? sender, KeyEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnProgramsKeyDown(sender, e);
}
