using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class StepPaletteView : UserControl
{
    public StepPaletteView() => InitializeComponent();
    public event EventHandler? InsertRequested;
    private void OnCancel(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
    private void OnInsert(object? sender, RoutedEventArgs e) => InsertRequested?.Invoke(this, EventArgs.Empty);
    internal void FocusSearch() => SearchBox.Focus();
}
