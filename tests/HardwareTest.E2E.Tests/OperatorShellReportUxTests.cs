using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Shell;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorShellReportUxTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Active_notification_keeps_contrast_when_theme_changes_without_a_new_message()
    {
        using var shown = E2EHarness.ShowMainWindow();
        var app = E2EHarness.RequireApp();
        var original = app.RequestedThemeVariant;
        try
        {
            shown.Main.ShellNotification.Clear();
            shown.Main.ShellNotification.Publish(ShellNotificationSeverity.Info, "Operator input is required.");
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            var card = shown.Window.FindControl<Border>("NotificationCard")!;
            var light = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var dark = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;
            Assert.NotEqual(light, dark);
            var text = shown.Window.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => AutomationProperties.GetName(control) == "Station notification message");
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
            Assert.True(Contrast(dark, foreground) >= 4.5);
            Assert.Equal(ShellNotificationSeverity.Info, shown.Main.ShellNotification.Severity);
            Assert.Equal("Operator input is required.", shown.Main.ShellNotification.Message);
            Assert.DoesNotContain("Ready", shown.Main.ShellNotification.IdleHint);
        }
        finally { app.RequestedThemeVariant = original; Dispatcher.UIThread.RunJobs(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Compact_report_actions_fit_and_full_status_is_selectable_without_changing_the_report()
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = 20;
        shown.Main.IsNavPaneOpen = false;
        shown.Main.NavigateToPageId("ReportPreview");
        var report = shown.Main.ReportPreview;
        report.Status = "Report action failed: " + string.Join("\n", Enumerable.Range(0, 12).Select(i => $"Complete diagnostic line {i}"));
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<ReportPreviewView>().Single();
        foreach (var action in view.GetVisualDescendants().OfType<Button>().Where(control => control.IsEffectivelyVisible))
            Inside(action, window);
        var details = view.GetVisualDescendants().OfType<Button>()
            .Single(control => AutomationProperties.GetName(control) == "Show full report details");
        var path = report.PdfPath;
        var summary = report.ReportSummary;
        details.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await E2EHarness.PumpUiAsync();
        var dialog = Assert.Single(window.OwnedWindows);
        var text = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(control => AutomationProperties.GetName(control) == "Full report details");
        Assert.True(text.IsReadOnly);
        Assert.Contains(report.Status, text.Text!);
        Assert.True(text.Focus()); text.SelectAll();
        Assert.Contains("Complete diagnostic line 11", text.SelectedText);
        var close = dialog.GetVisualDescendants().OfType<Button>()
            .Single(control => AutomationProperties.GetName(control) == "Close report details");
        Inside(close, dialog);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await E2EHarness.PumpUiAsync();
        Assert.Empty(window.OwnedWindows);
        Assert.True(details.IsFocused);
        Assert.Equal(path, report.PdfPath);
        Assert.Equal(summary, report.ReportSummary);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Active_step_filter_recolors_when_theme_changes_without_resetting_the_filter()
    {
        using var shown = E2EHarness.ShowMainWindow();
        shown.Main.NavigateToPageId("RunTest");
        var run = shown.Main.RunTest;
        run.SessionPanel.DutSerialInput = "FILTER-DUT";
        run.SessionPanel.OperatorInput = "Filter technician";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        run.StepTree.StepStatusFilter = HardwareTest.Features.RunTest.StepStatusFilter.Fail;
        var app = E2EHarness.RequireApp();
        var original = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            var banner = shown.Window.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Name == "ActiveStatusFilterBanner");
            Assert.True(banner.IsEffectivelyVisible);
            var light = Assert.IsAssignableFrom<ISolidColorBrush>(banner.Background).Color;
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var dark = Assert.IsAssignableFrom<ISolidColorBrush>(banner.Background).Color;
            var label = banner.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Text == run.StepTree.StatusFilterBannerText);
            var text = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
            Assert.NotEqual(light, dark);
            Assert.True(Contrast(dark, text) >= 4.5);
            Assert.Equal(HardwareTest.Features.RunTest.StepStatusFilter.Fail, run.StepTree.StepStatusFilter);
        }
        finally { app.RequestedThemeVariant = original; Dispatcher.UIThread.RunJobs(); }
    }

    private static void Inside(Control control, TopLevel window)
    {
        var point = control.TranslatePoint(default, window);
        Assert.NotNull(point);
        Assert.True(new Rect(window.ClientSize).Inflate(.75).Contains(new Rect(point.Value, control.Bounds.Size)),
            $"{control.GetType().Name} {control.Bounds} must fit inside {window.ClientSize}.");
    }

    private static double Contrast(Color left, Color right)
    {
        static double Channel(byte value) { var n = value / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var a = Luminance(left); var b = Luminance(right);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
