using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class MainWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _viewModel;

    public MainWindow()
        : this(new AuthoringWorkspaceViewModel())
    {
    }

    public MainWindow(AuthoringWorkspaceViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnOpenWorkspace(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Open authoring workspace",
                AllowMultiple = false,
            });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        TryRun(() => _viewModel.Open(path));
    }

    private void OnValidate(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.Validate(strict: true));

    private void OnSaveSidecar(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.SaveSidecar());

    private async void OnPack(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Pack output directory",
                AllowMultiple = false,
            });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        TryRun(() => _viewModel.Pack(path, new PackOptions { Offline = true }));
    }

    private void OnProgramSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is ProgramDraft draft)
        {
            _viewModel.SelectProgram(draft.PlanId);
        }
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _viewModel.ReportError(ex.Message);
        }
    }
}
