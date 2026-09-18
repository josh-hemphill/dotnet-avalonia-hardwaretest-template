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

            desktop.MainWindow = new MainWindow(vm);
            var workspace = desktop.Args?.FirstOrDefault(arg => !arg.StartsWith('-'));
            if (!string.IsNullOrWhiteSpace(workspace))
            {
                try
                {
                    vm.Open(workspace);
                }
                catch (Exception ex)
                {
                    vm.ReportError(ex.Message);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
