using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private Window? _commandPalette;
    private sealed record EditorCommand(string Id, string Title, string Shortcut, Func<string?> Blocker, Func<Task> Run)
    {
        public override string ToString() => Title;
    }
    private KeyModifiers PrimaryModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    private string PrimaryName => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
    private string? WorkspaceBlocker() => _viewModel.Workspace is null ? "Open a workspace first."
        : _viewModel.Workspace.IsReadOnly ? "This workspace is read-only." : _viewModel.OperationBusy || _viewModel.OperationCleanupPending ? "Finish or cancel the active operation and its cleanup first." : null;
    private static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
    private IReadOnlyList<EditorCommand> Commands() =>
    [
        new("open", "Open workspace", $"{PrimaryName}+O", () => _transitionInFlight ? "A workspace transition is pending." : null, async () => { await OpenWorkspaceAsync(); }),
        new("workspace", "Create workspace", $"{PrimaryName}+Shift+N", () => _viewModel.OperationBusy || _viewModel.OperationCleanupPending ? "Finish the active operation and its cleanup first." : null, ShowWorkspaceCreationAsync),
        new("plan", "New test plan", $"{PrimaryName}+N", () => _viewModel.CanInitializePlan ? null : "Open a writable workspace and finish the active operation.", ShowPlanInitializationAsync),
        new("save", "Save draft / compile", $"{PrimaryName}+S", () => WorkspaceBlocker() ?? (_viewModel.SelectedProgram is null ? "Select a program first." : null), Sync(_viewModel.Apply)),
        new("save-all", "Save all", $"{PrimaryName}+Shift+S", WorkspaceBlocker, Sync(() => _viewModel.SaveAll())),
        new("undo", "Undo program edit", $"{PrimaryName}+Z", () => _viewModel.CanUndo ? null : "No program edit to undo.", Sync(() => _viewModel.Undo())),
        new("redo", "Redo program edit", $"{PrimaryName}+Shift+Z", () => _viewModel.CanRedo ? null : "No program edit to redo.", Sync(() => _viewModel.Redo())),
        new("rename", "Rename selected step", "F2", () => _viewModel.CanRenameSequence ? null : "Select a named editable step.", RenameCommandAsync),
        new("duplicate", "Duplicate selected step", $"{PrimaryName}+D", () => _viewModel.CanDuplicateSequence ? null : "Select a duplicable editable step.", Sync(_viewModel.DuplicateSelectedSequence)),
        new("add", "Add selected recipe", $"{PrimaryName}+Shift+A", () => _viewModel.CanInsertRecipe ? null : _viewModel.SelectedRecipePrerequisites, Sync(_viewModel.InsertSelectedRecipe)),
        new("up", "Move step up", "Alt+Up", () => _viewModel.CanMoveSequence ? null : "Select an editable setup or measurement step.", Sync(() => _viewModel.MoveSelectedSequence(-1))),
        new("down", "Move step down", "Alt+Down", () => _viewModel.CanMoveSequence ? null : "Select an editable setup or measurement step.", Sync(() => _viewModel.MoveSelectedSequence(1))),
        new("issue", "Next issue", "F8", () => _viewModel.FindingRows.Count + _viewModel.EditingIssues.Count > 0 ? null : "No current editing issues or checked findings.", Sync(NextIssue)),
        new("overview", "Open workspace overview", "", () => _viewModel.HasWorkspace ? null : "Open a workspace first.", Sync(() => WorkspaceTabs.SelectedIndex = 8)),
        new("preview", "Open operator preview", "", () => _viewModel.HasWorkspace ? null : "Open a workspace first.", Sync(() => WorkspaceTabs.SelectedIndex = PreviewRouteIndex)),
        new("issues", "Toggle Issues drawer", "", () => _viewModel.PreferencesEditable ? null : "Future preferences are read-only.", Sync(() => _viewModel.IssuesDrawerOpen = !_viewModel.IssuesDrawerOpen)),
        new("reset", "Reset saved layout", "", () => _viewModel.PreferencesEditable ? null : "Future preferences are read-only.", Sync(ResetShellLayout)),
        new("tui", "Open saved plan in external TUI", "", TuiBlocker, LaunchTuiAsync)
    ];

    private void InitializePlanCommands()
    {
        AddHandler(KeyDownEvent, OnExpertKeyDown, RoutingStrategies.Bubble);
        ToolTip.SetTip(this.FindControl<Button>("CommandPaletteButton")!, $"{PrimaryName}+Shift+P");
    }
    internal void OnExpertKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var modifiers = e.KeyModifiers;
        if (e.Key == Key.P && modifiers == (PrimaryModifier | KeyModifiers.Shift))
        { OnOpenCommandPalette(this, new RoutedEventArgs()); e.Handled = true; return; }
        var textEditing = e.Source is Control control && (control is TextBox || control.GetVisualAncestors().Any(ancestor => ancestor is TextBox));
        if (textEditing && (e.Key is Key.Z or Key.Y or Key.D || modifiers == KeyModifiers.Alt)) return;
        var id = (e.Key, modifiers) switch
        {
            (Key.S, var m) when m == PrimaryModifier => "save",
            (Key.S, var m) when m == (PrimaryModifier | KeyModifiers.Shift) => "save-all",
            (Key.O, var m) when m == PrimaryModifier => "open",
            (Key.N, var m) when m == PrimaryModifier => "plan",
            (Key.N, var m) when m == (PrimaryModifier | KeyModifiers.Shift) => "workspace",
            (Key.Z, var m) when m == PrimaryModifier => "undo",
            (Key.Z, var m) when m == (PrimaryModifier | KeyModifiers.Shift) => "redo",
            (Key.Y, var m) when m == KeyModifiers.Control && !OperatingSystem.IsMacOS() => "redo",
            (Key.D, var m) when m == PrimaryModifier => "duplicate",
            (Key.A, var m) when m == (PrimaryModifier | KeyModifiers.Shift) => "add",
            (Key.Up, KeyModifiers.Alt) => "up",
            (Key.Down, KeyModifiers.Alt) => "down",
            (Key.F2, KeyModifiers.None) => "rename",
            (Key.F8, KeyModifiers.None) => "issue",
            _ => null
        };
        if (id is null) return;
        e.Handled = true;
        _ = ExecuteCommandAsync(id);
    }
    public async Task<bool> ExecuteCommandAsync(string id)
    {
        var current = OwnerContext();
        if (!current()) return false;
        var command = Commands().Single(candidate => candidate.Id == id);
        if (command.Blocker() is { } explanation) { _viewModel.ReportError(explanation); return false; }
        CommitFocusedEditor();
        try { await command.Run(); return true; }
        catch (Exception error) { if (current()) _viewModel.ReportError(error.Message); return false; }
    }
    internal async void OnOpenCommandPalette(object? sender, RoutedEventArgs e)
    {
        if (_commandPalette is not null) { _commandPalette.Activate(); return; }
        var current = OwnerContext();
        var search = new TextBox { PlaceholderText = "Search commands", MinHeight = 42 };
        AutomationProperties.SetName(search, "Search commands");
        var list = new ListBox { Classes = { "authoringList" }, Margin = new Thickness(8, 8, 8, 24), Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        list.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel { Margin = new Thickness(0, 0, 0, 16) });
        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EditorCommand>((command, _) =>
        {
            if (command is null) return new TextBlock();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            row.Children.Add(new TextBlock { Text = command.Title, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            var shortcut = new TextBlock { Text = command.Shortcut, Opacity = 0.65, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(shortcut, 1); row.Children.Add(shortcut);
            return row;
        });
        AutomationProperties.SetName(list, "Authoring commands");
        var hint = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var run = new Button { Content = "Run command", IsDefault = true, Classes = { "accent", "authoringAction" } };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Classes = { "authoringAction" } };
        var commands = Commands();
        IReadOnlyList<EditorCommand> visible = [];
        void Filter()
        {
            visible = commands.Where(command => $"{command.Title} {command.Shortcut}".Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = visible; list.SelectedIndex = visible.Count > 0 ? 0 : -1;
        }
        list.SelectionChanged += (_, _) =>
        {
            var selected = list.SelectedIndex >= 0 ? visible[list.SelectedIndex] : null;
            hint.Text = selected?.Blocker() ?? (selected is null ? "No matching commands." : "Ready");
            run.IsEnabled = selected is not null && selected.Blocker() is null;
        };
        search.TextChanged += (_, _) => Filter(); Filter();
        var dialog = new Window { Title = "Commands", Width = 560, Height = 540, FontSize = FontSize, MinWidth = 420, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Classes.Add("authoringPalette");
        var panel = new DockPanel();
        var header = new Border
        {
            Padding = new Thickness(24, 20),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Classes = { "paletteSection" },
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = "Commands", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold }, search }
            }
        };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, run }
        };
        var footer = new Border
        {
            Name = "CommandFooter",
            Padding = new Thickness(24, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Classes = { "paletteSection" },
            Child = new StackPanel { Spacing = 12, Children = { hint, actions } }
        };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(list); dialog.Content = panel;
        run.Click += (_, _) => { if (run.IsEnabled) dialog.Close(visible[list.SelectedIndex].Id); };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => search.Focus();
        _commandPalette = dialog;
        string? requested;
        try { requested = await dialog.ShowDialog<string?>(this); }
        finally { _commandPalette = null; }
        if (requested is not null && current()) await ExecuteCommandAsync(requested);
    }
    private async Task RenameCommandAsync()
    {
        var current = OwnerContext(); var node = _viewModel.SelectedSequence?.NodeId;
        var name = new TextBox { Text = _viewModel.SelectedSequence?.Label };
        AutomationProperties.SetName(name, "New step name");
        var accept = new Button { Content = "Rename", IsDefault = true };
        AutomationProperties.SetName(accept, "Rename");
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window { Title = "Rename step", Width = 400, Height = 180, Content = new StackPanel { Margin = new Thickness(16), Spacing = 8, Children = { name, accept, cancel } } };
        accept.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        dialog.Opened += (_, _) => name.Focus();
        if (await dialog.ShowDialog<bool>(this) && current() && node == _viewModel.SelectedSequence?.NodeId)
        { _viewModel.SequenceRename = name.Text ?? ""; _viewModel.RenameSelectedSequence(); }
    }
    private int _nextIssue;
    private void NextIssue()
    {
        var rows = _viewModel.EditingIssues.Cast<object>().Concat(_viewModel.FindingRows).ToArray();
        if (rows.Length == 0) return;
        var row = rows[_nextIssue++ % rows.Length];
        OnOpenFindingProgram(new Button { DataContext = row }, new RoutedEventArgs());
    }
}
