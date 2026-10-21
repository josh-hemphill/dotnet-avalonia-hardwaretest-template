using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceOverviewView : UserControl
{
    public WorkspaceOverviewView() => InitializeComponent();

    private void OnNavigate(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnNavigateTask(sender, e);
}

/// Task cards preserve reading order and use wider rows when larger text needs more room.
public sealed class WorkspaceTaskCards : Grid
{
    private int _columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = availableSize.Width >= 1080 && GetValue(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty) <= 16 ? 3 : availableSize.Width >= 700 ? 2 : 1;
        if (_columns != columns)
        {
            _columns = columns;
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", columns)));
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", (Children.Count + columns - 1) / columns)));
            ColumnSpacing = RowSpacing = 16;
            for (var i = 0; i < Children.Count; i++)
            {
                SetColumn(Children[i], i % columns);
                SetRow(Children[i], i / columns);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
