using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class WorkspacePreviewView : UserControl
{
    private readonly IAuthoringRecordingPicker _recordingPicker;
    private readonly ScrollViewer _sourceDetailsViewport;
    private readonly Grid _previewLayout;
    private readonly WrapPanel _sourceActions;
    private readonly StackPanel _recordingRows;
    private readonly OperatorPreviewPane _boardPane;
    public WorkspacePreviewView() : this(new AuthoringRecordingPicker()) { }
    public WorkspacePreviewView(IAuthoringRecordingPicker recordingPicker)
    {
        _recordingPicker = recordingPicker;
        InitializeComponent();
        _sourceDetailsViewport = this.FindControl<ScrollViewer>("SourceDetailsViewport")!;
        _previewLayout = this.FindControl<Grid>("PreviewLayout")!;
        _sourceActions = this.FindControl<WrapPanel>("SourceActions")!;
        _recordingRows = this.FindControl<StackPanel>("RecordingRows")!;
        _boardPane = this.FindControl<OperatorPreviewPane>("BoardPane")!;
        LayoutUpdated += ConstrainSourceViewport;
        _boardPane.AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
        {
            // The board's scroller reveals its widget; the outer frame must reveal the board too.
            if (!ReferenceEquals(e.TargetObject, _boardPane)) _boardPane.BringIntoView();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void ConstrainSourceViewport(object? sender, EventArgs e)
    {
        if (Bounds.Height <= 0 || !double.IsFinite(Bounds.Height)) return;
        // Keep recordings outside the provenance scroller and reserve the graphs' usable height.
        // Smaller busy layouts can scroll the frame without shrinking the board's own viewport.
        const double boardMinimum = 180;
        const double rowGaps = 16;
        var actionsHeight = Math.Max(_sourceActions.Bounds.Height, _sourceActions.DesiredSize.Height);
        var recordingsHeight = Math.Max(_recordingRows.Bounds.Height, _recordingRows.DesiredSize.Height);
        var contentHeight = Math.Max(Bounds.Height, actionsHeight + recordingsHeight + boardMinimum + rowGaps);
        var sourceMaximum = Math.Max(actionsHeight, contentHeight - recordingsHeight - boardMinimum - rowGaps);
        if (!double.IsFinite(_previewLayout.Height) || Math.Abs(_previewLayout.Height - contentHeight) > 0.5) _previewLayout.Height = contentHeight;
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
        var workspace = vm.Workspace;
        var program = vm.SelectedProgram;
        var session = vm.WorkspaceSessionId;
        bool CurrentSession() => owner.IsVisible && ReferenceEquals(DataContext, vm) && ReferenceEquals(workspace, vm.Workspace)
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

    private async void OnOpenRecordingFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AuthoringWorkspaceViewModel { Workspace: { } workspace } vm || TopLevel.GetTopLevel(this) is not { } owner) return;
        var session = vm.WorkspaceSessionId;
        bool Current() => owner.IsVisible && ReferenceEquals(workspace, vm.Workspace) && ReferenceEquals(DataContext, vm) && session == vm.WorkspaceSessionId;
        try
        {
            var path = RunDatasetCatalog.ResolveRecordingsRoot(workspace);
            if (!Directory.Exists(path)) { vm.ReportError("No recordings folder yet. Import a recording to create it."); return; }
            if (!owner.IsVisible || !ReferenceEquals(workspace, vm.Workspace) || !ReferenceEquals(DataContext, vm)) return;
            var opened = await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
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
