using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

public partial class MainWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _viewModel;
    private SettingsWindow? _settings;

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

    private void OnBootstrap(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.Bootstrap(new BootstrapOptions { Offline = true }));

    private void OnValidate(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.Validate(strict: true));

    private void OnSaveSidecar(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.SaveSidecar());

    private void OnApply(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.Apply());

    private void OnCreateProgram(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.CreateProgram());

    private void OnRemoveProgram(object? sender, RoutedEventArgs e)
        => TryRun(_viewModel.RemoveSelectedProgram);

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.ApplySelectedRecipe());

    private void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => TryRun(_viewModel.RemoveSelectedSequence);

    private void OnSequenceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !_viewModel.CanRemoveSelectedSequence)
        {
            return;
        }

        TryRun(_viewModel.RemoveSelectedSequence);
        e.Handled = true;
    }

    private void OnProgramsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !_viewModel.CanRemoveSelectedProgram)
        {
            return;
        }

        TryRun(_viewModel.RemoveSelectedProgram);
        e.Handled = true;
    }

    private async void OnImportTransferFunction(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import transfer function JSON",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Transfer function JSON")
                    {
                        Patterns = ["*.tf.json", "*.json"],
                    },
                ],
            });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        TryRun(() => _viewModel.ImportTransferFunction(path));
    }

    private void OnOpenLastWorkspace(object? sender, RoutedEventArgs e)
        => TryRun(_viewModel.OpenLastWorkspace);

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow
        {
            DataContext = _viewModel,
        };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show(this);
    }

    private void OnFormulaCaretChanged(object? sender, RoutedEventArgs e) => SyncFormulaCaret();

    private void SyncFormulaCaret()
    {
        if (this.FindControl<TextBox>("FormulaBox") is { } box)
        {
            _viewModel.RefreshFormulaCompletions(box.CaretIndex);
        }
    }

    private void OnFormulaChip(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token } || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var box = this.FindControl<TextBox>("FormulaBox");
        var caret = box?.CaretIndex ?? _viewModel.FormulaSource.Length;
        var next = _viewModel.ApplyFormulaCompletion(token, caret);
        if (box is not null)
        {
            box.CaretIndex = next;
            box.Focus();
        }

        _viewModel.RefreshFormulaCompletions(next);
    }

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
