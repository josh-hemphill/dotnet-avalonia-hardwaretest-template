using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class WorkspacePreviewView : UserControl
{
    private readonly IAuthoringRecordingPicker _recordingPicker;
    private readonly Func<TopLevel, DirectoryInfo, Task<bool>>? _launchRecordingFolder;
    private readonly ScrollViewer _sourceDetailsViewport;
    private readonly StackPanel _recordingRows;
    private readonly OperatorPreviewPane _boardPane;
    private readonly Grid _boardRegion;
    private readonly TextBlock _boardHeading;
    public WorkspacePreviewView() : this(new AuthoringRecordingPicker()) { }
    public WorkspacePreviewView(IAuthoringRecordingPicker recordingPicker, Func<TopLevel, DirectoryInfo, Task<bool>>? launchRecordingFolder = null)
    {
        _recordingPicker = recordingPicker;
        _launchRecordingFolder = launchRecordingFolder;
        InitializeComponent();
        _sourceDetailsViewport = this.FindControl<ScrollViewer>("SourceDetailsViewport")!;
        _recordingRows = this.FindControl<StackPanel>("RecordingRows")!;
        _boardPane = this.FindControl<OperatorPreviewPane>("BoardPane")!;
        _boardRegion = this.FindControl<Grid>("BoardRegion")!;
        _boardHeading = this.FindControl<TextBlock>("BoardHeading")!;
        LayoutUpdated += ConstrainSourceViewport;
    }

    private void ConstrainSourceViewport(object? sender, EventArgs e)
    {
        if (Bounds.Height <= 0 || !double.IsFinite(Bounds.Height)) return;
        // Source and board are sibling viewports. The board alone owns its scrolling.
        const double boardMinimum = 80;
        const double rowGaps = 16;
        var boardRegionMinimum = boardMinimum + Math.Max(_boardHeading.Bounds.Height, _boardHeading.DesiredSize.Height) + _boardRegion.RowSpacing;
        var recordingsHeight = Math.Max(_recordingRows.Bounds.Height, _recordingRows.DesiredSize.Height);
        var sourceMaximum = Math.Max(48, Math.Min(Bounds.Height * 0.35, Bounds.Height - recordingsHeight - boardRegionMinimum - rowGaps));
        if (Math.Abs(_boardPane.MinHeight - boardMinimum) > 0.5) _boardPane.MinHeight = boardMinimum;
        if (Math.Abs(_sourceDetailsViewport.MaxHeight - sourceMaximum) > 0.5) _sourceDetailsViewport.MaxHeight = sourceMaximum;
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
            vm.ImportRecording(path);
            return true;
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or IOException or UnauthorizedAccessException)
        {
            if (CurrentSession()) vm.ReportError(error.Message);
            return false;
        }
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
            if (!Directory.Exists(path)) { if (Current()) vm.ReportError("No recordings folder yet. Import a recording to create it."); return; }
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
