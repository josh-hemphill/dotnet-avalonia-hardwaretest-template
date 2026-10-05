using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private readonly IAuthoringWorkspacePicker? _packOutputPicker;
    private readonly IAuthoringWorkspacePicker? _offlinePackagePicker;
    private bool _buildPickerInFlight;

    internal async void OnPack(object? sender, RoutedEventArgs e) => await PickBuildOrImportAsync(false);
    internal async void OnImportOfflinePackage(object? sender, RoutedEventArgs e) => await PickBuildOrImportAsync(true);
    internal async void OnPrepareEnvironment(object? sender, RoutedEventArgs e) => await RunOperationAsync(AuthoringOperationKind.Bootstrap);

    private async Task PickBuildOrImportAsync(bool import)
    {
        if (_buildPickerInFlight || _ownerClosed || !IsVisible || _viewModel.OperationBusy || _viewModel.Workspace is null) return;
        CommitFocusedEditor();
        var vm = _viewModel;
        var workspace = vm.Workspace;
        var session = vm.WorkspaceSessionId;
        var home = vm.AuthoringHomeText;
        var outputIntent = vm.PackPreview.LastOutputDirectory;
        bool CurrentOwner() => !_ownerClosed && IsVisible && ReferenceEquals(DataContext, vm)
            && ReferenceEquals(vm.Workspace, workspace) && vm.WorkspaceSessionId == session
            && vm.AuthoringHomeText == home && vm.PackPreview.LastOutputDirectory == outputIntent && !vm.OperationBusy;
        string? path;
        _buildPickerInFlight = true;
        try
        {
            var picker = import ? _offlinePackagePicker : _packOutputPicker;
            path = picker is not null ? await picker.PickAsync() : import ? await PickOfflinePackageAsync() : await PickPackOutputAsync();
            if (string.IsNullOrWhiteSpace(path) || !CurrentOwner()) return;
        }
        catch (Exception error) { if (CurrentOwner()) vm.ReportError(error.Message); return; }
        finally { _buildPickerInFlight = false; }
        await RunOperationAsync(import ? AuthoringOperationKind.Bootstrap : AuthoringOperationKind.Pack,
            import ? null : path, import ? path : null);
    }
    private async Task<string?> PickOfflinePackageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import offline authoring package",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("OpenTAP package") { Patterns = ["*.TapPackage", "*.zip"] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickPackOutputAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pack output directory", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
