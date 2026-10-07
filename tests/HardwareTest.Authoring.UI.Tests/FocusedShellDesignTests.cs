using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class FocusedShellDesignTests
{
    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public void Welcome_centers_primary_creation_and_keeps_commands_centered(int width, int height)
    {
        using var fixture = new AuthoringUiFixture();
        var window = fixture.Show(width, height);
        var welcome = window.FindControl<Border>("WelcomeSurface")!;
        var create = fixture.Control<Button>("Create workspace from welcome");
        ResponsiveShellTests.Inside(create, window);
        Assert.Contains("accent", create.Classes);
        Assert.True(create.Bounds.Height >= 52);
        Assert.Equal(width / 2d, welcome.TranslatePoint(new Point(welcome.Bounds.Width / 2, 0), window)!.Value.X, 0);
        var commands = fixture.Control<Button>("Command palette");
        var label = commands.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Commands");
        var center = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), commands)!.Value.Y;
        Assert.Equal(commands.Bounds.Height / 2, center, 0);
        Assert.False(window.FindControl<Grid>("WorkspaceLayout")!.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public void Search_and_results_are_separate_and_last_command_is_reachable(int width, int height)
    {
        using var fixture = new AuthoringUiFixture();
        var window = fixture.Show(width, height);
        AuthoringUiFixture.Click(fixture.Control<Button>("Command palette"));
        var palette = Assert.Single(window.OwnedWindows);
        var search = fixture.Control<TextBox>("Search commands", palette);
        var results = fixture.Control<ListBox>("Authoring commands", palette);
        var origin = results.TranslatePoint(default, palette)!.Value;
        var searchBottom = search.TranslatePoint(new Point(0, search.Bounds.Height), palette)!.Value;
        Assert.True(origin.Y - searchBottom.Y >= 16);
        var footer = palette.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "CommandFooter");
        var resultsBottom = results.TranslatePoint(new Point(0, results.Bounds.Height), palette)!.Value.Y;
        Assert.True(footer.TranslatePoint(default, palette)!.Value.Y - resultsBottom >= 24);
        var last = results.Items.Cast<object>().Last();
        results.ScrollIntoView(last);
        AuthoringUiFixture.Drain();
        Assert.Single(results.GetVisualDescendants().OfType<ScrollViewer>()).ScrollToEnd();
        AuthoringUiFixture.Drain();
        var lastRow = (Control)results.ContainerFromIndex(results.ItemCount - 1)!;
        ResponsiveShellTests.Inside(lastRow, palette);
        var lastBottom = lastRow.TranslatePoint(new Point(0, lastRow.Bounds.Height), results)!.Value.Y;
        Assert.True(results.Bounds.Height - lastBottom >= 15.25, $"Last row gap {results.Bounds.Height - lastBottom}; padding {results.Padding}");
        search.Text = "Open saved plan";
        AuthoringUiFixture.Drain();
        Assert.Single(results.Items.Cast<object>());
        Assert.Contains(palette.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("workspace", StringComparison.OrdinalIgnoreCase) == true);
        var run = palette.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Run command"));
        Assert.False(run.IsEnabled);
        palette.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
    }
}
