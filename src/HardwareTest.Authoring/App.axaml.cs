using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HardwareTest.Authoring;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var store = new AuthoringPreferencesStore();
        string? loadError = null;
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }

        AuthoringThemeApplier.Apply(store.Current.ThemePreference);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new AuthoringWorkspaceViewModel(preferences: store);
            vm.ThemePreferenceChanged += AuthoringThemeApplier.Apply;
            if (loadError is not null)
            {
                vm.ReportError(loadError);
            }
            else if (!string.IsNullOrWhiteSpace(store.Warning))
            {
                vm.ReportError(store.Warning);
            }

            var mainWindow = new MainWindow(vm);
            desktop.MainWindow = mainWindow;
            var workspace = desktop.Args?.FirstOrDefault(arg => !arg.StartsWith('-'));
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                // Show the shell before importing OpenTAP programs so first launch has
                // the same progress feedback and ownership checks as later opens.
                mainWindow.Opened += async (_, _) => await mainWindow.OpenWorkspaceAsync(workspace);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
