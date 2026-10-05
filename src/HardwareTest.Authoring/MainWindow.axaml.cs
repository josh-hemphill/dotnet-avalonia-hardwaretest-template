using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _viewModel;
    private SettingsWindow? _settings;

    public MainWindow()
        : this(new AuthoringWorkspaceViewModel())
    {
    }

    public MainWindow(AuthoringWorkspaceViewModel viewModel, IAuthoringLifecycleInteraction? lifecycleInteraction = null, IAuthoringWorkspacePicker? workspacePicker = null, IAuthoringWorkspacePicker? packOutputPicker = null, IAuthoringWorkspacePicker? offlinePackagePicker = null)
    {
        _viewModel = viewModel;
        _packOutputPicker = packOutputPicker;
        _offlinePackagePicker = offlinePackagePicker;
        InitializeComponent();
        DataContext = viewModel;
        InitializeShell();
        InitializePlanCommands();
        InitializeGuidance();
        InitializeLifecycle(lifecycleInteraction, workspacePicker);
        _viewModel.ConfigureRecoveryDispatch(action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        _viewModel.ConfigureOperations(CreateOperationRunner(),
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        Closed += (_, _) => { _viewModel.StopRecovery(); _viewModel.StopOperations(); };
    }

    internal void OnAcceptRecovery(object? sender, RoutedEventArgs e)
        => TryRun(() => { if (_viewModel.SelectedRecoveryPlanId is { } id) _viewModel.AcceptRecovery(id); });
    internal void OnDiscardRecovery(object? sender, RoutedEventArgs e)
        => TryRun(() => { if (_viewModel.SelectedRecoveryPlanId is { } id) _viewModel.DiscardRecovery(id); });
    internal void OnImportCompiled(object? sender, RoutedEventArgs e)
        => TryRun(() => { if (_viewModel.SelectedProgram is { } p) _viewModel.ReconcileCompiled(p.PlanId, true); });
    internal void OnRetainSource(object? sender, RoutedEventArgs e)
        => TryRun(() => { if (_viewModel.SelectedProgram is { } p) _viewModel.ReconcileCompiled(p.PlanId, false); });

    private async void OnOpenWorkspace(object? sender, RoutedEventArgs e)
        => await OpenWorkspaceAsync();

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.Undo());
    }

    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.Redo());
    }
    private void OnUndoWorkspace(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.UndoWorkspace());
    }
    private void OnRedoWorkspace(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.RedoWorkspace());
    }

    private void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.SaveAll());
    }

    private async void OnBootstrap(object? sender, RoutedEventArgs e)
        => await RunOperationAsync(AuthoringOperationKind.Bootstrap);

    private async void OnValidate(object? sender, RoutedEventArgs e)
        => await RunOperationAsync(AuthoringOperationKind.Validate);

    private void OnSaveSidecar(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.SaveSidecar());
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        CommitFocusedEditor();
        TryRun(() => _viewModel.Apply());
    }

    internal async void OnCreateProgram(object? sender, RoutedEventArgs e)
        => await ShowPlanInitializationAsync();

    internal async void OnRemoveProgram(object? sender, RoutedEventArgs e)
        => await ConfirmRemoveProgramAsync();

    internal void OnAddRecipe(object? sender, RoutedEventArgs e)
        => TryRun(() => _viewModel.ApplySelectedRecipe());

    internal void OnToggleCleanupSlot(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AuthoringCatalogToggle row } box)
        {
            return;
        }

        TryRun(() => _viewModel.SetCleanupSlotIncluded(row.Id, box.IsChecked == true));
    }

    internal void OnMetricSettingLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: AuthoringSettingRow row } box)
        {
            TryRun(() => _viewModel.SetMetricSetting(row.Key, box.Text ?? string.Empty));
            return;
        }

        if (sender is ComboBox { DataContext: AuthoringSettingRow comboRow } combo)
        {
            var text = combo.SelectedItem as string ?? combo.Text ?? string.Empty;
            if (string.Equals(comboRow.Value, text, StringComparison.Ordinal) || !comboRow.ShouldCommitLostFocusText(text))
            {
                return;
            }

            TryRun(() => _viewModel.SetMetricSetting(comboRow.Key, text));
        }
    }

    internal void OnMetricSettingBoolChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: AuthoringSettingRow row } box)
        {
            return;
        }

        TryRun(() => _viewModel.SetMetricSettingBool(row.Key, box.IsChecked == true));
    }

    internal void OnMetricSettingChoiceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: AuthoringSettingRow row } box
            && box.SelectedItem is string selected
            && !string.Equals(row.Value, selected, StringComparison.Ordinal))
        {
            TryRun(() => _viewModel.SetMetricSetting(row.Key, selected));
        }
    }

    internal void OnMetricSettingNumberChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is NumericUpDown { DataContext: AuthoringSettingRow row })
        {
            TryRun(() => _viewModel.SetMetricSettingNumber(row.Key, e.NewValue));
        }
    }

    internal void OnRemoveSequence(object? sender, RoutedEventArgs e)
        => TryRun(_viewModel.RemoveSelectedSequence);

    internal void OnSequenceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !_viewModel.CanRemoveSelectedSequence)
        {
            return;
        }

        TryRun(_viewModel.RemoveSelectedSequence);
        e.Handled = true;
    }

    internal async void OnProgramsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || !_viewModel.CanRemoveSelectedProgram)
        {
            return;
        }

        e.Handled = true;
        await ConfirmRemoveProgramAsync();
    }

    internal async void OnImportTransferFunction(object? sender, RoutedEventArgs e)
    {
        var current = OwnerContext();
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
        if (string.IsNullOrWhiteSpace(path) || !current())
        {
            return;
        }

        TryRun(() => _viewModel.ImportTransferFunction(path));
    }

    internal async void OnOpenLastWorkspace(object? sender, RoutedEventArgs e)
        => await OpenWorkspaceAsync(_viewModel.LastWorkspacePath);

    private long _findingNavigationGeneration;

    internal void OnOpenFindingProgram(object? sender, RoutedEventArgs e)
    {
        var current = OwnerContext();
        var target = sender is Button { DataContext: AuthoringFindingRow row } ? _viewModel.NavigateFinding(row)
            : sender is Button { DataContext: AuthoringEditingIssue issue } ? _viewModel.NavigateEditingIssue(issue) : null;
        if (target is null) return;
        var navigation = ++_findingNavigationGeneration;
        var workspace = _viewModel.Workspace;
        var document = _viewModel.SelectedDocument;
        var revision = document?.Revision;
        WorkspaceTabs.SelectedIndex = target.NodeId is null ? 1 : 0;
        if (target.NodeId is not null && target.Section is null && target.Field is null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!current() || navigation != _findingNavigationGeneration || !ReferenceEquals(workspace, _viewModel.Workspace)
                || !ReferenceEquals(document, _viewModel.SelectedDocument) || revision != document?.Revision
                || target.ProgramId != _viewModel.SelectedProgram?.PlanId || target.NodeId != _viewModel.SelectedSequence?.NodeId) return;
            var inspector = this.GetVisualDescendants().OfType<SelectedStepInspectorView>().SingleOrDefault();
            if (target.NodeId is not null && inspector?.FocusFinding(target) != true)
                _viewModel.ReportError("The target section is open; no supported precise field control is available.");
        }, Avalonia.Threading.DispatcherPriority.Loaded);
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

    internal void OnFormulaCaretChanged(object? sender, RoutedEventArgs e) => SyncFormulaCaret();

    private void SyncFormulaCaret()
    {
        if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == "FormulaBox") is { } box)
        {
            _viewModel.RefreshFormulaCompletions(box.CaretIndex);
        }
    }

    internal void OnFormulaChip(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string token } || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == "FormulaBox");
        var caret = box?.CaretIndex ?? _viewModel.FormulaSource.Length;
        var next = _viewModel.ApplyFormulaCompletion(token, caret);
        if (box is not null)
        {
            box.CaretIndex = next;
            box.Focus();
        }

        _viewModel.RefreshFormulaCompletions(next);
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _viewModel.ReportError(AuthoringWorkspaceViewModel.PersistenceError(ex));
        }
    }
}
