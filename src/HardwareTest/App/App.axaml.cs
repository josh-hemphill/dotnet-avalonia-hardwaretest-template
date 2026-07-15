using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HardwareTest.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareTest;

public partial class App : Application
{
    private readonly ISettingsStore _settingsStore;
    private ServiceProvider? _services;

    /// Optional factory used by the parameterless ctor (headless / designer hooks).
    public static Func<ISettingsStore>? SettingsStoreFactory { get; set; }

    public App()
        : this(SettingsStoreFactory?.Invoke() ?? new SettingsStore())
    {
    }

    public App(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public ISettingsStore SettingsStore => _settingsStore;

    public ServiceProvider Services =>
        _services ?? throw new InvalidOperationException("DI container is not built yet.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeApplier.Apply(_settingsStore.AppSettings);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = Composition.Build(_settingsStore);
        ThemeApplier.Apply(_settingsStore.AppSettings);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = _services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += async (_, _) =>
            {
                try
                {
                    await _settingsStore.SaveUiStateAsync();
                    await _settingsStore.SaveAppSettingsAsync();
                }
                catch
                {
                    // best effort on shutdown
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
