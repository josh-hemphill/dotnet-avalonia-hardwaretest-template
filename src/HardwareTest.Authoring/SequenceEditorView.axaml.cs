using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView()
    {
        InitializeComponent();
        void RevealSelection() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (SequenceList.IsEffectivelyVisible && SequenceList.SelectedItem is { } selected) SequenceList.ScrollIntoView(selected);
        });
        SequenceList.SelectionChanged += (_, _) => RevealSelection();
        SequenceList.SizeChanged += (_, _) => RevealSelection();
    }

    internal void FocusAdd() => SequenceActionsButton.Focus();
    private void OnOpenPalette(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenStepPalette(sender, e);

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnSequenceKeyDown(sender, e);
}
