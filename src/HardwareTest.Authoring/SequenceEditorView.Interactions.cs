using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class SequenceEditorView
{
    private Guid? _dragNode;
    private Point _dragStart;
    private bool _dragging;

    private void InitializeSequenceInteractions()
    {
        SequenceList.AddHandler(PointerPressedEvent, OnSequencePointerPressed, RoutingStrategies.Tunnel);
        SequenceList.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_dragNode is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var delta = e.GetPosition(this) - _dragStart;
            if (delta.X * delta.X + delta.Y * delta.Y < 64) return;
            _dragging = true;
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }, RoutingStrategies.Tunnel);
        SequenceList.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var source = _dragNode;
            if (source is null) return;
            var wasDragging = _dragging;
            _dragNode = null;
            Cursor = null;
            e.Pointer.Capture(null);
            if (!wasDragging) return;
            _dragging = false;
            var target = RowAt(e.GetPosition(SequenceList));
            if (target?.NodeId is { } targetId) Vm?.ReorderSequence(source.Value, targetId);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        SequenceList.PointerCaptureLost += (_, _) => { _dragNode = null; _dragging = false; Cursor = null; };
    }

    private SequenceRow? RowAt(Point point)
        => SequenceList.GetVisualDescendants().OfType<ListBoxItem>()
            .FirstOrDefault(item => item.IsEffectivelyVisible && item.Bounds.Size.Width > 0
                && item.TranslatePoint(default, SequenceList) is { } origin
                && new Rect(origin, item.Bounds.Size).Contains(point))?.DataContext as SequenceRow;

    private void OnSequencePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source && (source is Button || source.GetVisualAncestors().OfType<Button>().Any())) return;
        if (Vm is not { } vm || RowAt(e.GetPosition(SequenceList)) is not { IsSelectable: true } row) return;
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(row));
            ShowSequenceMenu(vm);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed && vm.CanEditSequence && row.Section != SequenceSection.Cleanup)
        {
            vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(row));
            SequenceList.Focus();
            e.Pointer.Capture(SequenceList);
            _dragStart = e.GetPosition(this);
            _dragNode = row.NodeId;
            _dragging = false;
            e.Handled = true;
        }
    }

    private void ShowSequenceMenu(AuthoringWorkspaceViewModel vm)
    {
        var menu = new ContextMenu();
        MenuItem Action(string title, bool enabled, Action action)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        menu.Items.Add(Action(vm.HasSectionEditor ? "Edit section name…" : "Edit step name…", vm.CanRenameSequence,
            () => Inspector()?.FocusName()));
        if (vm.CanToggleSelectedSection)
            menu.Items.Add(Action(vm.SelectedSectionToggleLabel, true, vm.ToggleSelectedSection));
        menu.Items.Add(Action("Move to section…", vm.CanEditSequence && vm.SelectedSequence?.Section == SequenceSection.Measure,
            () => Inspector()?.FocusSectionDestination()));
        menu.Items.Add(Action(vm.HasSectionEditor ? "Duplicate section" : "Duplicate step", vm.CanDuplicateSequence, vm.DuplicateSelectedSequence));
        menu.Items.Add(Action("Move up", vm.CanMoveSequence, () => vm.MoveSelectedSequence(-1)));
        menu.Items.Add(Action("Move down", vm.CanMoveSequence, () => vm.MoveSelectedSequence(1)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action(vm.RemoveSequenceActionTitle, vm.CanRemoveSelectedSequence,
            () => OnRemoveSequence(this, new RoutedEventArgs())));
        menu.Open(SequenceList);
    }

    private SelectedStepInspectorView? Inspector() => (TopLevel.GetTopLevel(this) as MainWindow)?
        .GetVisualDescendants().OfType<SelectedStepInspectorView>().SingleOrDefault();

    private void OnJumpSection(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Button { Tag: string name }
            || !Enum.TryParse<SequenceSection>(name, out var section)) return;
        var first = vm.SequenceItems.FirstOrDefault(row => row.Section == section && row.IsSelectable);
        if (first is not null) vm.SelectSequence(vm.SequenceItems.ToList().IndexOf(first));
        var header = vm.SequenceItems.FirstOrDefault(row => row.Section == section);
        if (header is not null) SequenceList.ScrollIntoView(header);
        vm.RecipeSearch = string.Empty;
        vm.InsertionPosition = section == SequenceSection.Measure ? "End of Measure" : "End of section";
        var recipe = section switch
        {
            SequenceSection.Setup => AuthoringRecipeIds.Prompt,
            SequenceSection.Cleanup => AuthoringRecipeIds.Shutdown,
            _ => AuthoringRecipeIds.Acquire
        };
        vm.SelectedRecipe = vm.Recipes.FirstOrDefault(item => item.Id == recipe);
        SequenceList.Focus();
    }
}
