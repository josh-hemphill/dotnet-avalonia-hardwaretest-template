using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class ExpanderThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Painted_header_uses_theme_for_rest_hover_and_keyboard_focus_without_resizing(bool dark)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace(); fixture.NavigateTask(3);
        var expander = fixture.Control<Expander>("Instrument Components diagnostic details");
        expander.BringIntoView(); AuthoringUiFixture.Drain();
        var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ExpanderHeader");
        var painted = header.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToggleButtonBackground");
        Color Brush(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
        Assert.Equal(Color.Parse(dark ? "#202938" : "#FFFFFF"), Brush(painted.Background));
        Assert.Equal(new Thickness(12, 10), header.Padding);
        var width = expander.Bounds.Width;
        var center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center); AuthoringUiFixture.Drain();
        Assert.Equal(Color.Parse(dark ? "#151B25" : "#F3F5F8"), Brush(painted.Background));
        Assert.True(header.Focus(NavigationMethod.Tab)); AuthoringUiFixture.Drain();
        Assert.Equal(Color.Parse(dark ? "#7EA8FF" : "#245ECA"), Brush(painted.BorderBrush));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.True(expander.IsExpanded);
        Assert.Equal(width, expander.Bounds.Width);
        window.MouseMove(new Point(2, 2)); AuthoringUiFixture.Drain();
        Assert.Equal(Color.Parse(dark ? "#202938" : "#FFFFFF"), Brush(painted.Background));
    }
}
