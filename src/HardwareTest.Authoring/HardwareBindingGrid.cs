using Avalonia;
using Avalonia.Controls;

namespace HardwareTest.Authoring;

/// Keeps selection and its draft editor adjacent whenever both retain usable width.
public sealed class HardwareBindingGrid : Grid
{
    private bool? _stacked;

    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = availableSize.Width < 680;
        if (_stacked != stacked)
        {
            _stacked = stacked;
            ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "220,*");
            RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            ColumnSpacing = 20;
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
