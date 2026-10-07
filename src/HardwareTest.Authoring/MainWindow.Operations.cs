using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private static AuthoringChildProcessRunner CreateOperationRunner()
    {
        var assembly = typeof(Program).Assembly.Location;
        if (string.IsNullOrEmpty(assembly))
            return AuthoringChildProcessRunner.ForExecutable(Environment.ProcessPath
                ?? throw new InvalidOperationException("The authoring executable path is unavailable."));
        var appHost = Path.Combine(Path.GetDirectoryName(assembly)!, "HardwareTest.Authoring" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        return AuthoringChildProcessRunner.ForExecutable(File.Exists(appHost) ? appHost : assembly);
    }

    private async Task RunOperationAsync(AuthoringOperationKind kind, string? outputDirectory = null, string? offlinePackagePath = null)
    {
        if (_viewModel.OperationBusy || _ownerClosed || !IsVisible || !ReferenceEquals(DataContext, _viewModel)) return;
        var workspace = _viewModel.Workspace;
        var session = _viewModel.WorkspaceSessionId;
        var home = _viewModel.AuthoringHomeText;
        try { await _viewModel.RunOperationAsync(kind, outputDirectory, offlinePackagePath); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (ReferenceEquals(workspace, _viewModel.Workspace) && session == _viewModel.WorkspaceSessionId && home == _viewModel.AuthoringHomeText && !_ownerClosed && IsVisible) _viewModel.ReportError(error.Message); }
    }
    private void OnCancelOperation(object? sender, RoutedEventArgs e) => _viewModel.CancelOperation();
}
