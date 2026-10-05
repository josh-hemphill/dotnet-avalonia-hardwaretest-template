using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private const int PreviewRouteIndex = 5;
    private readonly WorkspacePreviewView _previewView = new();
    private readonly WorkspaceIssuesView _issuesView = new();
    private readonly Dictionary<int, Control> _routeFocus = [];

    private void InitializeShell()
    {
        var workspaceMenu = (Flyout)this.FindControl<Button>("LifecycleFocusTarget")!.Flyout!;
        ((Control)workspaceMenu.Content!).AddHandler(Button.ClickEvent, (_, _) => workspaceMenu.Hide());
        ((Control)workspaceMenu.Content!).AddHandler(KeyDownEvent, (_, e) =>
        {
            OnExpertKeyDown(this, e);
            if (e.Handled) workspaceMenu.Hide();
        });
        _previewView.DataContext = _viewModel;
        _issuesView.DataContext = _viewModel;
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
            ArrangeShell();
            var current = OwnerContext();
            var route = WorkspaceTabs.SelectedIndex;
            Dispatcher.UIThread.Post(() =>
            {
                if (current() && WorkspaceTabs.SelectedIndex == route && _routeFocus.TryGetValue(route, out var control) && control.IsEffectivelyVisible)
                {
                    control.BringIntoView();
                    control.Focus();
                }
            });
        };
        _viewModel.PropertyChanged += (_, _) => ArrangeShell();
        ArrangeShell();
    }

    private void ArrangeShell()
    {
        // Reserve editor width before offering the optional dock. Larger type uses the separate route.
        SaveAllFeedback.IsVisible = _viewModel.SaveAllResults.Count > 0;
        ProgramsRail.IsVisible = !_viewModel.ProgramsRailCollapsed;
        WorkspaceLayout.ColumnDefinitions[0].Width = new GridLength(_viewModel.ProgramsRailCollapsed ? 0 : 196);
        WorkspaceTabs.Padding = new Avalonia.Thickness(_viewModel.ProgramsRailCollapsed ? 0 : 212, 0, 0, 0);
        var drawer = _viewModel.IssuesDrawerOpen && WorkspaceTabs.SelectedIndex != 2;
        var issuesDestination = drawer ? IssuesDrawer : IssuesRoute;
        var previousIssues = drawer ? IssuesRoute : IssuesDrawer;
        if (!ReferenceEquals(issuesDestination.Content, _issuesView))
        { previousIssues.Content = null; issuesDestination.Content = _issuesView; }
        IssuesDrawer.IsVisible = drawer;
        DependencySummary.Text = _viewModel.SelectedDependencySummary;
        var dock = _viewModel.DockPreview && ClientSize.Width >= 1280 && FontSize <= 16;
        var destination = dock && WorkspaceTabs.SelectedIndex != PreviewRouteIndex ? DockedPreview : SeparatePreview;
        var previous = ReferenceEquals(destination, DockedPreview) ? SeparatePreview : DockedPreview;
        if (!ReferenceEquals(destination.Content, _previewView))
        {
            previous.Content = null;
            destination.Content = _previewView;
        }
        ProgramLayout.ColumnDefinitions[4].Width = new GridLength(dock ? 300 : 0);
        ProgramLayout.ColumnDefinitions[3].Width = new GridLength(dock ? 16 : 0);
        DockedPreviewHeading.IsVisible = dock;
        DockedPreview.IsVisible = dock;
    }
}
