using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class SequenceEditorView : UserControl
{
    public SequenceEditorView()
    {
        InitializeComponent();
        InitializeSequenceInteractions();
        SequenceList.AddHandler(KeyDownEvent, OnSequenceKeyDown, RoutingStrategies.Tunnel);
        void RevealSelection() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (SequenceList.IsEffectivelyVisible && SequenceList.SelectedItem is { } selected) SequenceList.ScrollIntoView(selected);
        });
        SequenceList.SelectionChanged += (_, _) => RevealSelection();
        SequenceList.SizeChanged += (_, _) => RevealSelection();
        var actions = (Flyout)SequenceActionsButton.Flyout!;
        actions.Opened += (_, _) => ((ScrollViewer)actions.Content!).FontSize = FontSize;
        ((Control)actions.Content!).AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button button && button.Classes.Contains("sequenceAction")) actions.Hide();
        });
        ((Control)actions.Content!).AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            (TopLevel.GetTopLevel(this) as MainWindow)?.OnExpertKeyDown(this, e);
            if (e.Handled) actions.Hide();
        });
    }

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;
    private void OnRenameSequence(object? sender, RoutedEventArgs e) => Vm?.RenameSelectedSequence();
    private void OnDuplicateSequence(object? sender, RoutedEventArgs e) => Vm?.DuplicateSelectedSequence();
    private void OnMoveSequenceUp(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(-1);
    private void OnMoveSequenceDown(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedSequence(1);

    private void OnAddSection(object? sender, RoutedEventArgs e) => Vm?.AddSection();
    private void OnExpandAllSections(object? sender, RoutedEventArgs e) => Vm?.ExpandAllSections();
    private void OnMoveToSection(object? sender, RoutedEventArgs e) => Vm?.MoveSelectedToSection();

    private void OnToggleSection(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Control { DataContext: SequenceRow row }) return;
        vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(row));
        vm.ToggleSelectedSection();
        FocusSelectedRow();
        e.Handled = true;
    }

    private void FocusSelectedRow() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (Vm?.SelectedSequence is { } selected)
            (SequenceList.ContainerFromItem(selected) as Control)?.Focus();
    });

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnAddRecipe(sender, e);

    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveSequence(sender, e);

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && Vm is { SelectedSequence.IsSection: true } sectionVm && e.Key is Key.Left or Key.Right)
        {
            if (sectionVm.SelectedSequence.IsExpanded == (e.Key == Key.Left)) sectionVm.ToggleSelectedSection();
            FocusSelectedRow();
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift)
            && Vm is { SelectedSequence.IsSelectable: true } vm)
        {
            ShowSequenceMenu(vm);
            e.Handled = true;
            return;
        }
        (TopLevel.GetTopLevel(this) as MainWindow)?.OnSequenceKeyDown(sender, e);
    }
}
