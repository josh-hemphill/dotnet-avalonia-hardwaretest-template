using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView() => InitializeComponent();

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnAddRecipe(sender, e);

    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveSequence(sender, e);

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnSequenceKeyDown(sender, e);
}
