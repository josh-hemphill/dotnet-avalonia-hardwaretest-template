using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Features.ReportPreview;

public partial class ReportPreviewView : UserControl
{
    private Window? _details;

    public ReportPreviewView()
    {
        InitializeComponent();
    }

    private async void OnShowDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportPreviewViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (_details is not null) { _details.Activate(); return; }
        var fullText = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { vm.ReportSummary, vm.Status, vm.PrintStatus, vm.PdfPath }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());
        var text = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(fullText) ? "No report is loaded." : fullText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(text, "Full report details");
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Disabled);
        var close = new Button { Content = "Close", IsCancel = true, Classes = { "operator" } };
        AutomationProperties.SetName(close, "Close report details");
        var footer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { close }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var panel = new DockPanel { Margin = new Thickness(20), Children = { footer, text } };
        var dialog = new Window
        {
            Title = "Report details",
            Width = Math.Min(760, owner.ClientSize.Width - 48),
            Height = Math.Min(520, owner.ClientSize.Height - 48),
            MinWidth = 360,
            MinHeight = 260,
            FontSize = owner.FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => text.Focus();
        _details = dialog;
        try { await dialog.ShowDialog(owner); }
        finally { _details = null; }
        if (sender is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } trigger) trigger.Focus();
    }
}
