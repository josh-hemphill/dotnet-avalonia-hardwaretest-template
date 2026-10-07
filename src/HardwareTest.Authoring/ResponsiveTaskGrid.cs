using Avalonia;
using Avalonia.Controls;

namespace HardwareTest.Authoring;

/// Keeps a catalog and its edit task adjacent when space permits, then stacks them in reading order.
public sealed class ResponsiveTaskGrid : Grid
{
    private bool? _stacked;

    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = availableSize.Width < 740 || GetValue(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty) > 16;
        if (_stacked != stacked)
        {
            _stacked = stacked;
            ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "2*,3*");
            RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            ColumnSpacing = 16;
            RowSpacing = 16;
            for (var index = 0; index < Children.Count; index++)
            {
                SetColumn(Children[index], stacked ? 0 : index);
                SetRow(Children[index], stacked ? index : 0);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
