using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

internal static class BenchReferenceDialog
{
    public static async Task ShowAsync(Window owner, string title, Control body)
    {
        var window = new Window
        {
            Title = title,
            Width = Math.Max(320, Math.Min(820, owner.Bounds.Width - 40)),
            Height = Math.Max(320, Math.Min(700, owner.Bounds.Height - 40)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontSize = owner.FontSize,
            MinWidth = 320,
            MinHeight = 320,
        };
        var close = new Button { Content = "Close", MinHeight = 40, Margin = new Thickness(16, 0, 16, 12), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();
        Avalonia.Automation.AutomationProperties.SetName(close, "Close golden reference dialog");
        DockPanel.SetDock(close, Dock.Bottom);
        var root = new DockPanel(); root.Children.Add(close); root.Children.Add(body); window.Content = root;
        KeyboardNavigation.SetTabNavigation(window, KeyboardNavigationMode.Cycle);
        window.KeyDown += (_, args) => { if (args.Key == Key.Escape) { window.Close(); args.Handled = true; } };
        window.Opened += (_, _) => window.Dispatcher.Post(() =>
        {
            var input = body.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && !control.IsReadOnly);
            var choice = body.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Items.Count > 0);
            var acquisition = body.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
            ((Control?)input ?? choice ?? (Control?)acquisition ?? close).Focus();
        });
        await window.ShowDialog(owner);
    }
}
