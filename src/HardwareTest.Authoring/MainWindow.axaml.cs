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

    private async void OnRemoveProgram(object? sender, RoutedEventArgs e)
        => await ConfirmRemoveProgramAsync();

    private void OnAddRecipe(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.ApplySelectedRecipe());

    private void OnToggleCleanupSlot(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            return;
        }

        TryRun(() => _viewModel.SetCleanupSlotIncluded(row.Id, box.IsChecked == true));
    }

    private void OnMetricSettingLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: AuthoringSettingRow row } box)
        {
            TryRun(() => _viewModel.SetMetricSetting(row.Key, box.Text ?? string.Empty));
            return;
        }

        if (sender is ComboBox { DataContext: AuthoringSettingRow comboRow } combo)
        {
            var text = combo.SelectedItem as string ?? combo.Text ?? string.Empty;
            if (!comboRow.ShouldCommitLostFocusText(text))
            {
                return;
            }

            TryRun(() => _viewModel.SetMetricSetting(comboRow.Key, text));
        }
    }

    private void OnMetricSettingBoolChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AuthoringSettingRow row } box)
        {
            return;
        }

        TryRun(() => _viewModel.SetMetricSettingBool(row.Key, box.IsChecked == true));
    }

    private void OnMetricSettingChoiceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AuthoringSettingRow row } box
            && box.SelectedItem is string selected)
        {
            TryRun(() => _viewModel.SetMetricSetting(row.Key, selected));
        }
    }

    private void OnMetricSettingNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is NumericUpDown { DataContext: AuthoringSettingRow row })
        {
            TryRun(() => _viewModel.SetMetricSettingNumber(row.Key, e.NewValue));
        }
    }

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

    private async void OnProgramsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !_viewModel.CanRemoveSelectedProgram)
        {
            return;
        }

        e.Handled = true;
        await ConfirmRemoveProgramAsync();
    }

    private async Task ConfirmRemoveProgramAsync()
    {
        (string PlanId, string TapPlanPath, string SidecarPath) target;
        try
        {
            target = _viewModel.DescribeSelectedProgramRemoval();
        }
        catch (Exception ex)
        {
            _viewModel.ReportError(ex.Message);
            return;
        }

        var dialog = new Window
        {
            Title = "Remove program?",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = "Cancel", IsDefault = true };
        var remove = new Button { Content = "Remove program" };
        cancel.Click += (_, _) => dialog.Close(false);
        remove.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"Remove {target.PlanId} from this workspace?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"These files will be deleted if they exist:\n{target.TapPlanPath}\n{target.SidecarPath}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, remove } },
            },
        };

        if (await dialog.ShowDialog<bool>(this))
        {
            TryRun(() => _viewModel.RemoveSelectedProgramIfMatches(target));
        }
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

    private void OnOpenFindingProgram(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AuthoringFindingRow { CanOpenProgram: true } row })
        {
            return;
        }

        _viewModel.SelectProgram(row.ProgramId);
        if (this.FindControl<TabControl>("WorkspaceTabs") is { } tabs)
        {
            tabs.SelectedIndex = 0;
        }
    }

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
