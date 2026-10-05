using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(HardwareTest.Authoring.UI.Tests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HardwareTest.Authoring.UI.Tests;

// Keep this assembly's Avalonia lifecycle independent of operator E2E and production App.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<AuthoringTestApplication>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class AuthoringTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://HardwareTest.Authoring/"))
        {
            Source = new Uri("avares://HardwareTest.Authoring/AuthoringStyles.axaml")
        });
    }
}
