using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class WorkspacePreviewView : UserControl
{
    private readonly IAuthoringRecordingPicker _recordingPicker;
    private readonly Func<TopLevel, DirectoryInfo, Task<bool>>? _launchRecordingFolder;
    private Window? _sourceDetailsWindow;
    private RecordingPickerWindow? _recordingSelectionWindow;

    public WorkspacePreviewView() : this(new AuthoringRecordingPicker()) { }

    public WorkspacePreviewView(IAuthoringRecordingPicker recordingPicker,
        Func<TopLevel, DirectoryInfo, Task<bool>>? launchRecordingFolder = null)
    {
        _recordingPicker = recordingPicker;
        _launchRecordingFolder = launchRecordingFolder;
        InitializeComponent();
    }

    private async void OnShowSourceDetails(object? sender, RoutedEventArgs e)
        => await ShowDetailsAsync(sender, reference: false);

    private async void OnShowReferenceDetails(object? sender, RoutedEventArgs e)
        => await ShowDetailsAsync(sender, reference: true);

    private async Task ShowDetailsAsync(object? sender, bool reference)
    {
        if (_sourceDetailsWindow is not null) { _sourceDetailsWindow.Activate(); return; }
        if (DataContext is not AuthoringWorkspaceViewModel vm
            || TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
        if (owner is MainWindow && !ReferenceEquals(owner.DataContext, vm)) return;
        if (!vm.CanInspectPreviewDetails || reference && !vm.HasRecordedReferences) return;
        var details = new TextBox
        {
            Text = reference ? vm.RecordingReferenceReview : string.Join(Environment.NewLine + Environment.NewLine,
                new[] { vm.DataSourceDetails, vm.RecordingEmptyState }.Where(value => !string.IsNullOrWhiteSpace(value))),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(details, reference ? "Full recorded reference review" : "Full preview data source");
        ScrollViewer.SetHorizontalScrollBarVisibility(details, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(details, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var close = new Button
        {
            Content = "Close",
            ContentTemplate = AuthoringActionLabels.WrappedText,
            IsCancel = true,
            Classes = { "authoringAction" }
        };
        AutomationProperties.SetName(close, reference ? "Close recorded reference review" : "Close preview data source");
        var footer = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { close }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var panel = new DockPanel { Margin = new Thickness(20) };
        panel.Children.Add(footer);
        panel.Children.Add(details);
        var dialog = new Window
        {
            Title = reference ? "Recorded reference review" : "Preview data source",
            Width = Math.Min(760, owner.ClientSize.Width - 60),
            Height = Math.Min(560, owner.ClientSize.Height - 60),
            MinWidth = 400,
            MinHeight = 300,
            FontSize = owner.FontSize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            Classes = { "authoringPalette" }
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => details.Focus();
        _sourceDetailsWindow = dialog;
        try { await dialog.ShowDialog(owner); }
        finally { _sourceDetailsWindow = null; }
        RestoreTriggerFocus(owner, vm, sender);
    }

    private async void OnChooseRecording(object? sender, RoutedEventArgs e)
    {
        if (_recordingSelectionWindow is not null) { _recordingSelectionWindow.Activate(); return; }
        if (DataContext is not AuthoringWorkspaceViewModel vm
            || TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner || !vm.HasRecordings) return;
        var ownerDataContext = owner.DataContext;
        if (owner is MainWindow && !ReferenceEquals(ownerDataContext, vm)) return;
        var workspace = vm.Workspace;
        var program = vm.SelectedProgram;
        var session = vm.WorkspaceSessionId;
        var datasets = vm.Datasets;
        var labels = vm.DatasetItems;
        bool Current() => owner.IsVisible && ReferenceEquals(owner.DataContext, ownerDataContext)
            && ReferenceEquals(TopLevel.GetTopLevel(this), owner) && ReferenceEquals(DataContext, vm)
            && ReferenceEquals(workspace, vm.Workspace) && ReferenceEquals(program, vm.SelectedProgram)
            && session == vm.WorkspaceSessionId;
        var descriptions = datasets.Select((dataset, index) => dataset.Identity?.DisplayDetails ?? labels[index]).ToArray();
        var dialog = new RecordingPickerWindow(labels, vm.SelectedDatasetIndex, descriptions)
        {
            Width = Math.Min(580, owner.ClientSize.Width - 60),
            Height = Math.Min(500, owner.ClientSize.Height - 60),
            FontSize = owner.FontSize
        };
        _recordingSelectionWindow = dialog;
        try
        {
            var result = await dialog.ShowDialog<int?>(owner);
            if (result is not int index || !Current() || index < 0 || index >= datasets.Count) return;
            var currentIndex = vm.Datasets.ToList().FindIndex(item => ReferenceEquals(item, datasets[index]));
            if (currentIndex >= 0) vm.SelectedDatasetIndex = currentIndex;
            else vm.ReportError("The recording list changed. Open Recordings and choose the recording again.");
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or UnauthorizedAccessException)
        {
            if (Current()) vm.ReportError(error.Message);
        }
        finally
        {
            _recordingSelectionWindow = null;
            RestoreTriggerFocus(owner, vm, sender);
        }
    }

    private void RestoreTriggerFocus(Window owner, AuthoringWorkspaceViewModel vm, object? sender)
    {
        if (owner is MainWindow && !ReferenceEquals(owner.DataContext, vm)) return;
        if (owner.IsVisible && ReferenceEquals(TopLevel.GetTopLevel(this), owner) && ReferenceEquals(DataContext, vm)
            && sender is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } trigger) trigger.Focus();
    }

    private void OnUseExampleData(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AuthoringWorkspaceViewModel vm) vm.UseExampleData();
    }

    private async void OnImportRecording(object? sender, RoutedEventArgs e) => await ImportRecordingAsync();

    public async Task<bool> ImportRecordingAsync()
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm || !vm.CanImportRecording || TopLevel.GetTopLevel(this) is not { IsVisible: true } owner) return false;
        var ownerDataContext = owner.DataContext;
        if (owner is MainWindow && !ReferenceEquals(ownerDataContext, vm)) return false;
        var workspace = vm.Workspace;
        var program = vm.SelectedProgram;
        var session = vm.WorkspaceSessionId;
        bool CurrentSession() => owner.IsVisible && ReferenceEquals(owner.DataContext, ownerDataContext)
            && ReferenceEquals(TopLevel.GetTopLevel(this), owner) && ReferenceEquals(DataContext, vm) && ReferenceEquals(workspace, vm.Workspace)
            && ReferenceEquals(program, vm.SelectedProgram) && session == vm.WorkspaceSessionId && vm.CanImportRecording;
        try
        {
            var path = await _recordingPicker.PickAsync(owner);
            if (string.IsNullOrWhiteSpace(path) || !CurrentSession()) return false;
            return await vm.ImportRecordingAsync(path, ownerIsCurrent: CurrentSession);
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or UnauthorizedAccessException)
        {
            if (CurrentSession()) vm.ReportError(error.Message);
            return false;
        }
    }

    private async void OnRefreshRecordings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthoringWorkspaceViewModel vm || TopLevel.GetTopLevel(this) is not { IsVisible: true } owner) return;
        var session = vm.WorkspaceSessionId;
        bool Current() => owner.IsVisible && ReferenceEquals(DataContext, vm) && session == vm.WorkspaceSessionId;
        try { await vm.RefreshRecordingsAsync(Current); }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or UnauthorizedAccessException)
        { if (Current()) vm.ReportError(error.Message); }
    }

    private async void OnOpenRecordingFolder(object? sender, RoutedEventArgs e) => await OpenRecordingFolderAsync();

    public async Task OpenRecordingFolderAsync()
    {
        if (DataContext is not AuthoringWorkspaceViewModel { Workspace: { } workspace } vm || TopLevel.GetTopLevel(this) is not { IsVisible: true } owner) return;
        var ownerDataContext = owner.DataContext;
        if (owner is MainWindow && !ReferenceEquals(ownerDataContext, vm)) return;
        var session = vm.WorkspaceSessionId;
        bool Current() => owner.IsVisible && ReferenceEquals(owner.DataContext, ownerDataContext)
            && ReferenceEquals(TopLevel.GetTopLevel(this), owner) && ReferenceEquals(workspace, vm.Workspace) && ReferenceEquals(DataContext, vm) && session == vm.WorkspaceSessionId;
        try
        {
            var path = RunDatasetCatalog.ResolveRecordingsRoot(workspace);
            if (!await Task.Run(() => Directory.Exists(path))) { if (Current()) vm.ReportError("No recordings folder yet. Import a recording to create it."); return; }
            if (!Current()) return;
            var opened = await (_launchRecordingFolder?.Invoke(owner, new DirectoryInfo(path))
                ?? owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)));
            if (Current() && !opened) vm.ReportError("Could not open the recordings folder.");
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or UnauthorizedAccessException) { if (Current()) vm.ReportError(error.Message); }
    }
}

public interface IAuthoringRecordingPicker
{
    Task<string?> PickAsync(TopLevel owner);
}

public sealed class AuthoringRecordingPicker : IAuthoringRecordingPicker
{
    public async Task<string?> PickAsync(TopLevel owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import recording run.json",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Recording JSON") { Patterns = ["*.json"] }],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
