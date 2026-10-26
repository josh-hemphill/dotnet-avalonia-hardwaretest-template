using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceEnvironmentView : UserControl
{
    public WorkspaceEnvironmentView() => InitializeComponent();

    private async void OnOpenPackageRepository(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm || TopLevel.GetTopLevel(this) is not { } owner) return;
        if (vm.PackageRepositoryError is { } error) { PackageSourceFeedback.Text = error; return; }
        try
        {
            var opened = await owner.Launcher.LaunchUriAsync(new Uri(vm.PackageRepositoryUrl));
            PackageSourceFeedback.Text = opened ? "Repository opened. Download matching package versions, then import the archives."
                : "Could not open a browser. Copy package requirements to get the repository URL.";
        }
        catch (Exception) { PackageSourceFeedback.Text = "Could not open a browser. Copy package requirements to get the repository URL."; }
    }

    private async void OnOpenLibraryReleases(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return;
        try
        {
            var opened = await owner.Launcher.LaunchUriAsync(new Uri("https://github.com/josh-hemphill/instrument-components/releases"));
            PackageSourceFeedback.Text = opened ? "Release page opened in your browser. Match the required package version before importing."
                : "Could not open a browser. Copy package requirements to get the release URL.";
        }
        catch (Exception) { PackageSourceFeedback.Text = "Could not open a browser. Copy package requirements to get the release URL."; }
    }

    private async void OnCopyPackageRequirements(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                PackageSourceFeedback.Text = "Clipboard unavailable. Read the required package names and versions below.";
                return;
            }
            await clipboard.SetTextAsync(vm.PackageRequirementsText);
            PackageSourceFeedback.Text = "Copied package names, versions, installed states, repository URL and library release URL.";
        }
        catch (Exception) { PackageSourceFeedback.Text = "Could not copy requirements. Read the required package names and versions below."; }
    }

    private void OnDeclareLibrary(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm) return;
        try { vm.DeclareLibraryDependency(); }
        catch (Exception error) { vm.ReportError(AuthoringWorkspaceViewModel.PersistenceError(error)); }
    }

    private void OnResumeInitialization(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnResumeInitialization(sender, e);

    private void OnPrepareEnvironment(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnPrepareEnvironment(sender, e);

    private void OnDownloadPackages(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnDownloadPackages(sender, e);

    private void OnCancelPreparation(object? sender, RoutedEventArgs e)
        => (DataContext as AuthoringWorkspaceViewModel)?.CancelOperation();

    private void OnImportOfflinePackage(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportOfflinePackage(sender, e);

    private void OnAcceptRecovery(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnAcceptRecovery(sender, e);

    private void OnDiscardRecovery(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnDiscardRecovery(sender, e);

    private void OnImportCompiled(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnImportCompiled(sender, e);

    private void OnRetainSource(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnRetainSource(sender, e);
}
