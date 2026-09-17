using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringChromeA11yTests
{
    [Fact]
    public void Program_window_has_sequence_inspector_preview_and_list_tab_once()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HardwareTest.Authoring", "MainWindow.axaml"));
        Assert.Contains("Text=\"{Binding SequenceTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding InspectorTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding PreviewTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Program settings\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.TabNavigation=\"Once\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LabeledBy", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SequenceItems}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OnFormulaChip", xaml, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
