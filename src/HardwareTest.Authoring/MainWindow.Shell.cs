using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private const int PreviewRouteIndex = 5;
    private readonly Stack<int> _routeHistory = new();
    private int _previousRoute;
    private bool _returningToRoute;

    private void OnNavigateBack(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.LoadingBusy || !_routeHistory.TryPop(out var route)) return;
        CommitFocusedEditor();
        _returningToRoute = true;
        try { WorkspaceTabs.SelectedIndex = route; }
        finally { _returningToRoute = false; }
        UpdateBackNavigation();
    }

    private void UpdateBackNavigation()
        => RouteBackButton.IsEnabled = _routeHistory.Count > 0 && !_viewModel.LoadingBusy;
    private readonly WorkspacePreviewView _previewView = new();
    private readonly WorkspaceIssuesView _issuesView = new();
    private readonly Dictionary<int, Control> _routeFocus = [];
    private double _inspectorWidth = 400;
    private bool _arrangingShell;
    private Guid _shellSession;
    private string? _shellPlan;

    private void InitializeShell()
    {
        foreach (var name in new[] { "LifecycleFocusTarget", "PlanActionsButton", "TaskMenuButton" })
        {
            var trigger = this.FindControl<Button>(name)!;
            var menu = (Flyout)trigger.Flyout!;
            menu.Opened += (_, _) => ((ScrollViewer)menu.Content!).FontSize = FontSize;
            ((Control)menu.Content!).AddHandler(Button.ClickEvent, (_, _) => menu.Hide());
            ((Control)menu.Content!).AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Handled) return;
                OnExpertKeyDown(this, e);
                if (e.Handled) menu.Hide();
            });
        }
        _previousRoute = WorkspaceTabs.SelectedIndex;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt && _routeHistory.Count > 0)
            { OnNavigateBack(this, new RoutedEventArgs()); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        UpdateBackNavigation();
        _previewView.DataContext = _viewModel;
        _issuesView.DataContext = _viewModel;
        SeparatePreview.Content = _previewView;
        SizeChanged += (_, _) => ArrangeShell();
        PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) ArrangeShell(); };
        WorkspaceTabs.AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is Control control and not TabItem and not TabControl)
                _routeFocus[WorkspaceTabs.SelectedIndex] = control;
        });
        WorkspaceTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, WorkspaceTabs)) return;
            if (!_returningToRoute && _previousRoute != WorkspaceTabs.SelectedIndex)
                _routeHistory.Push(_previousRoute);
            _previousRoute = WorkspaceTabs.SelectedIndex;
            UpdateBackNavigation();
            ArrangeShell();
            var current = OwnerContext();
            var plan = _viewModel.SelectedProgram?.PlanId;
            var route = WorkspaceTabs.SelectedIndex;
            Dispatcher.UIThread.Post(() =>
            {
                if (current() && plan == _viewModel.SelectedProgram?.PlanId && WorkspaceTabs.SelectedIndex == route
                    && _routeFocus.TryGetValue(route, out var control) && control.IsEffectivelyVisible)
                {
                    control.BringIntoView();
                    control.Focus();
                }
                else if (current() && plan == _viewModel.SelectedProgram?.PlanId && WorkspaceTabs.SelectedIndex == route
                         && route is 1 or 3 or 4 or 6 or 7 or 8)
                {
                    // A newly shown catalog list can ask its enclosing page to scroll to its
                    // selection. First visits should start at the task heading; revisits restore focus.
                    var root = (WorkspaceTabs.SelectedItem as TabItem)?.Content as Control;
                    var viewport = root?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                    if (viewport is not null) { viewport.Offset = default; viewport.Focusable = true; viewport.Focus(); }
                }
            }, DispatcherPriority.Loaded);
        };
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.WorkspaceSessionId)
                or nameof(AuthoringWorkspaceViewModel.SelectedProgram)
                or nameof(AuthoringWorkspaceViewModel.SaveAllResults)
                or nameof(AuthoringWorkspaceViewModel.IssuesDrawerOpen)) ArrangeShell();
        };
        ProgramLayout.ColumnDefinitions[2].PropertyChanged += (_, e) =>
        {
            if (!_arrangingShell && e.Property == ColumnDefinition.WidthProperty)
            {
                _inspectorWidth = ProgramLayout.ColumnDefinitions[2].Width.Value;
                ArrangeShell();
            }
        };
        ArrangeShell();
    }

    private void ArrangeShell()
    {
        if (_arrangingShell) return;
        _arrangingShell = true;
        try
        {
            if (_shellSession != _viewModel.WorkspaceSessionId)
            {
                _shellSession = _viewModel.WorkspaceSessionId;
                _routeFocus.Clear();
                WorkspaceTabs.SelectedIndex = 8;
                _routeHistory.Clear();
                _previousRoute = 8;
                UpdateBackNavigation();
            }
            if (_shellPlan != _viewModel.SelectedProgram?.PlanId)
            {
                _shellPlan = _viewModel.SelectedProgram?.PlanId;
                _routeFocus.Clear();
            }

            var drawer = _viewModel.IssuesDrawerOpen && WorkspaceTabs.SelectedIndex != 2;
            var issuesDestination = drawer ? IssuesDrawer : IssuesRoute;
            var previousIssues = drawer ? IssuesRoute : IssuesDrawer;
            if (!ReferenceEquals(issuesDestination.Content, _issuesView))
            { previousIssues.Content = null; issuesDestination.Content = _issuesView; }
            IssuesDrawer.IsVisible = drawer;
            var compact = ClientSize.Height <= 700;

            PlanNavigationBar.Margin = new Avalonia.Thickness(0, 0, 0, compact ? 8 : 16);
            // The sequence is the main canvas. Keep enough room for both panes, including enlarged text.
            foreach (var task in PlanNavigationBar.Children.OfType<ToggleButton>())
            {
                var selected = Equals(task.Tag, WorkspaceTabs.SelectedIndex.ToString());
                task.IsChecked = selected;
                task.IsVisible = !(ClientSize.Width < 1120 && FontSize > 16 && task.Tag is "2" or "4");
                AutomationProperties.SetHelpText(task, selected ? "Current task" : "Open this task");
            }
            var overflow = ClientSize.Width < 1120 && FontSize > 16;
            PlanNavigationBar.ColumnDefinitions[6].Width = overflow ? new GridLength(0) : new GridLength(.85, GridUnitType.Star);
            PlanNavigationBar.ColumnDefinitions[7].Width = overflow ? new GridLength(0) : new GridLength(.75, GridUnitType.Star);
            var taskName = (WorkspaceTabs.SelectedItem as TabItem)?.Header?.ToString() ?? "Overview";
            var taskCaption = WorkspaceTabs.SelectedIndex is 6 or 8 || overflow && WorkspaceTabs.SelectedIndex is 2 or 4 ? taskName + " ▾" : "More ▾";
            if (!Equals(TaskMenuButton.Content, taskCaption)) TaskMenuButton.Content = taskCaption;
            Title = $"{taskName} — {_viewModel.DisplayName} · HardwareTest Authoring";
            var available = Math.Max(0, ClientSize.Width - 32 - 16);
            var inspector = ProgramLayout.ColumnDefinitions[2];
            inspector.MinWidth = FontSize > 16 ? 360 : 320;
            inspector.MaxWidth = Math.Max(inspector.MinWidth, available - 360);
            inspector.Width = new GridLength(Math.Clamp(_inspectorWidth, inspector.MinWidth, inspector.MaxWidth));
            ProgramLayout.ColumnDefinitions[0].Width = GridLength.Star;
        }
        finally { _arrangingShell = false; }
    }

    internal void OnNavigateTask(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var route))
        {
            CommitFocusedEditor();
            WorkspaceTabs.SelectedIndex = route;
            ArrangeShell();
        }
    }

    internal void OpenDefinitions(string section)
    {
        CommitFocusedEditor();
        WorkspaceTabs.SelectedIndex = 6;
        var current = OwnerContext();
        Dispatcher.UIThread.Post(() =>
        {
            if (!current() || WorkspaceTabs.SelectedIndex != 6) return;
            var definitions = this.GetVisualDescendants().OfType<WorkspaceDefinitionsView>().SingleOrDefault();
            definitions?.FindControl<Border>(section)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void SwitchEditingPane()
    {
        if (WorkspaceTabs.SelectedIndex != 0) return;
        var focused = FocusManager?.GetFocusedElement() as Control;
        var fromSequence = focused is not null && focused.GetVisualAncestors().OfType<SequenceEditorView>().Any();
        CommitFocusedEditor();
        if (fromSequence)
            this.GetVisualDescendants().OfType<SelectedStepInspectorView>().SingleOrDefault()?.FocusPrimaryEditor();
        else
        {
            var list = this.GetVisualDescendants().OfType<SequenceEditorView>().SingleOrDefault()?.FindControl<ListBox>("SequenceList");
            if (list is { IsEffectivelyVisible: true })
            {
                if (list.SelectedItem is { } selected) list.ScrollIntoView(selected);
                list.Focus();
            }
        }
    }

    private void ResetShellLayout()
    {
        _inspectorWidth = 400;
        _viewModel.ResetLayout();
        ArrangeShell();
    }
}
