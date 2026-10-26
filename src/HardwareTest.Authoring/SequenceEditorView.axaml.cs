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
        var sections = (Flyout)SectionActionsButton.Flyout!;
        sections.Opened += (_, _) => ((ScrollViewer)sections.Content!).FontSize = FontSize;
        ((Control)sections.Content!).AddHandler(Button.ClickEvent, (_, _) => sections.Hide());
    }

    private AuthoringWorkspaceViewModel? Vm => DataContext as AuthoringWorkspaceViewModel;

    private void OnAddSection(object? sender, RoutedEventArgs e) => Vm?.AddSection();
    private void OnExpandAllSections(object? sender, RoutedEventArgs e) => Vm?.ExpandAllSections();

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

    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRemoveSequence(sender, e);
    internal void FocusAdd() => SequenceActionsButton.Focus();
    private void OnOpenPalette(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenStepPalette(sender, e);

    private void OnCreatePlan(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnCreateProgram(sender, e);

    private void OnConfigureInstruments(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) owner.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
    }

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
