using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView() => InitializeComponent();

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    private void OnRenameSequence(object? sender, RoutedEventArgs e) => Vm?.RenameSelectedSequence();
    private void OnDuplicateSequence(object? sender, RoutedEventArgs e) => Vm?.DuplicateSelectedSequence();
    private void OnMoveSequenceUp(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(-1);
    private void OnMoveSequenceDown(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(1);

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnAddRecipe(sender, e);

    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveSequence(sender, e);

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnSequenceKeyDown(sender, e);
}
