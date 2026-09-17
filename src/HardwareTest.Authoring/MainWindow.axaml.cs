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

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.ApplySelectedRecipe());

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

    private void OnFormulaChip(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token } || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (this.FindControl<TextBox>("FormulaBox") is { } box)
        {
            var text = box.Text ?? string.Empty;
            var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
            box.Text = text.Insert(caret, token);
            box.CaretIndex = caret + token.Length;
            _viewModel.FormulaSource = box.Text;
            return;
        }

        TryRun(() => _viewModel.InsertFormulaToken(token));
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

    private void OnProgramSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is ProgramDraft draft)
        {
            _viewModel.SelectProgram(draft.PlanId);
        }
    }

    private void OnRecipeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is AuthoringRecipe recipe)
        {
            _viewModel.SelectedRecipeId = recipe.Id;
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
