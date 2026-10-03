using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace HardwareTest.Authoring;

// Zero is deliberately the safe result when a modal is closed without choosing.
public enum UnsavedChangesChoice { Cancel = 0, SaveAll, Discard }
public interface IAuthoringLifecycleInteraction
{
    Task<UnsavedChangesChoice> ChooseAsync(IReadOnlyList<DirtyProgramSummary> dirtyPrograms);
}
public interface IAuthoringWorkspacePicker
{
    Task<string?> PickAsync();
}

public partial class MainWindow
{
    private IAuthoringLifecycleInteraction _lifecycleInteraction = null!;
    private IAuthoringWorkspacePicker _workspacePicker = null!;
    private bool _transitionInFlight;
    private bool _closeApproved;

    private void InitializeLifecycle(IAuthoringLifecycleInteraction? interaction, IAuthoringWorkspacePicker? picker)
    {
        _lifecycleInteraction = interaction ?? new AuthoringLifecycleInteraction(this);
        _workspacePicker = picker ?? new AuthoringWorkspacePicker(this);
        Closing += OnLifecycleClosing;
    }

    private void CommitFocusedEditor() => this.FindControl<Button>("LifecycleFocusTarget")?.Focus();

    public async Task<bool> OpenWorkspaceAsync(string? path = null)
    {
        if (_transitionInFlight) return false;
        _transitionInFlight = true;
        try
        {
            path ??= await _workspacePicker.PickAsync();
            if (string.IsNullOrWhiteSpace(path)) return false;
            CommitFocusedEditor();
            var decision = await ChooseTransitionAsync();
            if (decision == UnsavedChangesChoice.Cancel) return false;
            var prepared = _viewModel.PrepareOpen(path);
            _viewModel.CommitOpen(prepared, discardUnsavedChanges: decision == UnsavedChangesChoice.Discard);
            return true;
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); return false; }
        finally { _transitionInFlight = false; }
    }

    public Task<bool> ReopenWorkspaceAsync() => OpenWorkspaceAsync(_viewModel.Workspace?.Root);

    private async Task<UnsavedChangesChoice> ChooseTransitionAsync()
    {
        if (!_viewModel.HasUnsavedChanges) return UnsavedChangesChoice.Discard;
        var choice = await _lifecycleInteraction.ChooseAsync(_viewModel.DirtyPrograms);
        if (choice == UnsavedChangesChoice.SaveAll && !_viewModel.SaveAll().Succeeded)
            return UnsavedChangesChoice.Cancel;
        return choice is UnsavedChangesChoice.SaveAll or UnsavedChangesChoice.Discard ? choice : UnsavedChangesChoice.Cancel;
    }

    private void OnLifecycleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved) return;
        CommitFocusedEditor();
        if (_transitionInFlight) { e.Cancel = true; return; }
        if (!_viewModel.HasUnsavedChanges) return;
        e.Cancel = true;
        _transitionInFlight = true;
        _ = DecideCloseAsync();
    }

    private async Task DecideCloseAsync()
    {
        try
        {
            var decision = await ChooseTransitionAsync();
            if (decision == UnsavedChangesChoice.Cancel) { _transitionInFlight = false; return; }
            // Even completed injected choices must unwind the first Closing event.
            Dispatcher.UIThread.Post(() =>
            {
                _closeApproved = true;
                try { Close(); }
                finally { _closeApproved = false; _transitionInFlight = false; }
            });
        }
        catch (Exception ex) { _viewModel.ReportError(ex.Message); _transitionInFlight = false; }
    }
}

public sealed class AuthoringWorkspacePicker(Window owner) : IAuthoringWorkspacePicker
{
    public async Task<string?> PickAsync()
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Open authoring workspace", AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}

public sealed class AuthoringLifecycleInteraction(Window owner) : IAuthoringLifecycleInteraction
{
    public Task<UnsavedChangesChoice> ChooseAsync(IReadOnlyList<DirtyProgramSummary> dirtyPrograms)
    {
        var dialog = new Window
        {
            Title = "Unsaved programs",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        var save = new Button { Content = "Save all" };
        var discard = new Button { Content = "Discard" };
        cancel.Click += (_, _) => dialog.Close(UnsavedChangesChoice.Cancel);
        save.Click += (_, _) => dialog.Close(UnsavedChangesChoice.SaveAll);
        discard.Click += (_, _) => dialog.Close(UnsavedChangesChoice.Discard);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(UnsavedChangesChoice.Cancel); e.Handled = true; }
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Save edited programs before continuing?", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = string.Join(Environment.NewLine, dirtyPrograms.Select(p => p.PlanId)), TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, discard, save } },
            },
        };
        dialog.Opened += (_, _) => cancel.Focus();
        return dialog.ShowDialog<UnsavedChangesChoice>(owner);
    }
}
