using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceIssuesView : UserControl
{
    public WorkspaceIssuesView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) =>
        {
            // The entire task can scroll when editing is expanded. Keep checked rows
            // in a bounded list that can itself fit the minimum-size route viewport.
            var viewport = this.FindControl<ScrollViewer>("TaskViewport")!;
            var list = this.FindControl<ListBox>("CheckedFindingsList")!;
            if (list.TranslatePoint(default, viewport) is not { } position) return;
            var contentTop = position.Y + viewport.Offset.Y;
            var height = Math.Clamp(viewport.Viewport.Height - contentTop - 8, 96, 320);
            if (Math.Abs(list.Height - height) > 0.5) list.Height = height;
        };
    }

    private void OnOpenFindingProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenFindingProgram(sender, e);
}
