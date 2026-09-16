using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HardwareTest.Authoring;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new AuthoringWorkspaceViewModel();
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
