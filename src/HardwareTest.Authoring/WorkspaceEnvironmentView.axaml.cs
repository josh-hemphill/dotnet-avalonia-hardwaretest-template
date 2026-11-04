using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class WorkspaceEnvironmentView : UserControl
{
    private AuthoringWorkspaceViewModel? _observed;
    private string? _sourceError;
    private bool? _readinessStacked;

    public WorkspaceEnvironmentView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveEnvironment();
        AttachedToVisualTree += (_, _) => ObserveEnvironment();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_observed is not null) _observed.PropertyChanged -= OnEnvironmentChanged;
            _observed = null;
        };
        SizeChanged += (_, _) => ArrangeReadiness();
    }

    private void ArrangeReadiness()
    {
        var stacked = Bounds.Width < 680;
        if (_readinessStacked == stacked) return;
        _readinessStacked = stacked;
        ReadinessGrid.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,*");
        ReadinessGrid.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
        Grid.SetColumn(ExecutionReadinessCard, stacked ? 0 : 1);
        Grid.SetRow(ExecutionReadinessCard, stacked ? 1 : 0);
    }

    private void ObserveEnvironment()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnEnvironmentChanged;
        _observed = DataContext as AuthoringWorkspaceViewModel;
        _sourceError = null;
        if (_observed is not null) _observed.PropertyChanged += OnEnvironmentChanged;
        RevealSourceError();
        ArrangeReadiness();
    }

    private void OnEnvironmentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(AuthoringWorkspaceViewModel.Error)
            or nameof(AuthoringWorkspaceViewModel.PackageRepositoryError)) RevealSourceError();
    }

    private void RevealSourceError()
    {
        var error = !string.IsNullOrWhiteSpace(_observed?.Error) ? _observed.Error : _observed?.PackageRepositoryError;
        if (!string.IsNullOrEmpty(error) && error != _sourceError) PackageSources.IsExpanded = true;
        _sourceError = error;
    }

    private void OnShowPackageSources(object? sender, RoutedEventArgs e)
    {
        PackageSources.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!PackageSources.IsEffectivelyVisible || !PackageSources.IsExpanded) return;
            RepositoryBox.BringIntoView();
            if (!RepositoryBox.Focus()) PackageSources.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnChangeHome(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenAuthoringHomeSettings();

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
                PackageSourceFeedback.Text = "Clipboard unavailable. Read the required package names and versions above.";
                return;
            }
            await clipboard.SetTextAsync(vm.PackageRequirementsText);
            PackageSourceFeedback.Text = "Copied package names, versions, installed states, repository URL and library release URL.";
        }
        catch (Exception) { PackageSourceFeedback.Text = "Could not copy requirements. Read the required package names and versions above."; }
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
