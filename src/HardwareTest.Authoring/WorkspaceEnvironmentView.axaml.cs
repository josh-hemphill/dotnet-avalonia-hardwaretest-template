using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceEnvironmentView : UserControl
{
    public WorkspaceEnvironmentView() => InitializeComponent();

    private void OnPrepareEnvironment(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnPrepareEnvironment(sender, e);

    private void OnImportOfflinePackage(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportOfflinePackage(sender, e);

    private void OnAcceptRecovery(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnAcceptRecovery(sender, e);

    private void OnDiscardRecovery(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnDiscardRecovery(sender, e);

    private void OnImportCompiled(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportCompiled(sender, e);

    private void OnRetainSource(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRetainSource(sender, e);
}
