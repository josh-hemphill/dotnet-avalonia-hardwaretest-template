using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

// Review evidence is opt-in; ordinary tests retain the fast drawing backend.
public sealed class GraphicalReviewCapture
{
    public static bool CaptureEnabled => Environment.GetEnvironmentVariable("HT_CAPTURE_UI") == "1";

    [AvaloniaTheory(SkipUnless = nameof(CaptureEnabled))]
    [InlineData("Light", 1280, 800, 14)]
    [InlineData("Dark", 1280, 800, 14)]
    [InlineData("Light", 960, 600, 20)]
    [InlineData("Dark", 960, 600, 20)]
    public async Task Capture_authoring_tasks_and_empty_plan(string theme, int width, int height, int font)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = font;
        AuthoringUiFixture.Drain();
        var suffix = $"{theme.ToLowerInvariant()}-{width}x{height}-font{font}";
        Capture(window, $"welcome-{suffix}");
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        var routes = new (int Index, string Name)[]
        {
            (8, "overview"), (0, "sequence"), (1, "instruments"), (7, "operator-reports"),
            (3, "environment"), (4, "build"), (2, "issues"), (6, "definitions"), (5, "preview")
        };
        foreach (var route in routes)
        {
            fixture.NavigateTask(route.Index);
            if (route.Index == 5) fixture.ViewModel.UseExampleData();
            await fixture.WaitForReadinessAsync();
            Capture(window, $"{route.Name}-{suffix}");
        }
        fixture.NavigateTask(0);
        AuthoringUiFixture.Click(fixture.Control<Button>("Resume guidance"));
        GuidanceUiActions.WaitForAssessment(fixture);
        var guidance = window.FindControl<Border>("GuidanceHost")!;
        Assert.True(guidance.IsEffectivelyVisible);
        Capture(window, $"guidance-{suffix}");
        foreach (var group in guidance.GetVisualDescendants().OfType<Expander>()) group.IsExpanded = true;
        AuthoringUiFixture.Drain();
        Capture(window, $"guidance-expanded-{suffix}");
        AuthoringUiFixture.Click(fixture.Control<Button>("Close guidance"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        Capture(palette, $"step-palette-{suffix}");
        fixture.Control<TextBox>("Search sequence palette", palette).Text = "no-such-step-zzzz";
        AuthoringUiFixture.Drain();
        Capture(palette, $"step-search-empty-{suffix}");
        palette.Close();
        AuthoringUiFixture.Drain();
        fixture.ViewModel.InitializePlan(new("ui-review-empty"));
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(0);
        Capture(window, $"empty-plan-{suffix}");
    }

    private static void Capture(Window window, string name)
    {
        AuthoringUiFixture.Drain();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        var directory = Environment.GetEnvironmentVariable("HT_CAPTURE_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "ui-review");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
