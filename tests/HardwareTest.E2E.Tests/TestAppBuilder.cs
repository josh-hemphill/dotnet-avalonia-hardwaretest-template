using Avalonia;
using Avalonia.Headless;
using ReactiveUI.Avalonia;
using HardwareTest;
using HardwareTest.Core.Settings;

[assembly: AvaloniaTestApplication(typeof(HardwareTest.E2E.Tests.TestAppBuilder))]

namespace HardwareTest.E2E.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.SettingsStoreFactory = CreateIsolatedSettingsStore;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            })
            .WithInterFont()
            .UseReactiveUI(_ => { });
    }

    private static ISettingsStore CreateIsolatedSettingsStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "HardwareTestE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new SettingsStore(root);
        store.LoadAsync().GetAwaiter().GetResult();
        store.AppSettings.UseMockVisa = true;
        return store;
    }
}
