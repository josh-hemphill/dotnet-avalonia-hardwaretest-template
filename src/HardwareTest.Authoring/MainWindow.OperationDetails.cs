using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private Window? _operationDetails;

    private async void OnShowOperationDetails(object? sender, RoutedEventArgs e)
    {
        if (_operationDetails is not null) { _operationDetails.Activate(); return; }
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { _viewModel.Error, string.Join(Environment.NewLine, _viewModel.SaveAllResults), _viewModel.OperationLogText }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var details = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(text) ? _viewModel.Status : text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        ScrollViewer.SetVerticalScrollBarVisibility(details, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(details, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(details, "Full operation details");
        var close = new Button { Content = "Close", IsCancel = true, Classes = { "authoringAction" } };
        AutomationProperties.SetName(close, "Close operation details");
        var panel = new DockPanel { Margin = new Thickness(20) };
        var footer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { close }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(details);
        var dialog = new Window
        {
            Title = "Operation details",
            Width = Math.Min(760, ClientSize.Width - 60),
            Height = Math.Min(560, ClientSize.Height - 60),
            MinWidth = 400,
            MinHeight = 300,
            FontSize = FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            Classes = { "authoringPalette" }
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => details.Focus();
        _operationDetails = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _operationDetails = null; }
        if (IsVisible && sender is Control { IsEffectivelyVisible: true } target) target.Focus();
    }
}
