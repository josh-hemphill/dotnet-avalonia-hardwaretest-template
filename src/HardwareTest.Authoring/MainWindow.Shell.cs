using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private const int PreviewRouteIndex = 5;
    private readonly WorkspacePreviewView _previewView = new();
    private readonly Dictionary<int, Control> _routeFocus = [];

    private void InitializeShell()
    {
        _previewView.DataContext = _viewModel;
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
            Dispatcher.UIThread.Post(() =>
            {
                if (_routeFocus.TryGetValue(WorkspaceTabs.SelectedIndex, out var control) && control.IsEffectivelyVisible)
                {
                    control.BringIntoView();
                    control.Focus();
                }
            });
        };
        ArrangeShell();
    }

    private void ArrangeShell()
    {
        // Reserve editor width before offering the optional dock. Larger type uses the separate route.
        var dock = ClientSize.Width >= 1180 && FontSize <= 16;
        var destination = dock && WorkspaceTabs.SelectedIndex != PreviewRouteIndex ? DockedPreview : SeparatePreview;
        var previous = ReferenceEquals(destination, DockedPreview) ? SeparatePreview : DockedPreview;
        if (!ReferenceEquals(destination.Content, _previewView))
        {
            previous.Content = null;
            destination.Content = _previewView;
        }
        ProgramLayout.ColumnDefinitions[4].Width = new GridLength(dock ? 300 : 0);
        ProgramLayout.ColumnDefinitions[3].Width = new GridLength(0);
        DockedPreviewHeading.IsVisible = dock;
        DockedPreview.IsVisible = dock;
    }
}
