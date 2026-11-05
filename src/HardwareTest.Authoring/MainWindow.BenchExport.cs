using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private Window? _benchExport;

    internal async void OnPrepareBenchFiles(object? sender, RoutedEventArgs e)
    {
        if (_benchExport is not null) { _benchExport.Activate(); return; }
        CommitFocusedEditor();
        var current = OwnerContext();
        if (!current()) return;
        if (_viewModel.Workspace is null || _viewModel.SelectedProgram is null)
        { _viewModel.ReportError("Select a saved program before preparing bench files."); return; }
        try
        {
            var dialog = new BenchExportWindow(_viewModel, current)
            {
                FontSize = FontSize,
                Width = Math.Clamp(Bounds.Width - 40, 420, 680),
                Height = Math.Clamp(Bounds.Height - 40, 360, 620),
            };
            _benchExport = dialog;
            await dialog.ShowDialog(this);
        }
        catch (Exception error) { if (current()) _viewModel.ReportError(error.Message); }
        finally { _benchExport = null; if (current() && sender is Control trigger) trigger.Focus(); }
    }
}
