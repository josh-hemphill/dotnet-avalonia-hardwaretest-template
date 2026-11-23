using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public partial class ProcedureEditorWindow : Window
{
    private readonly ProcedureEditorSession _session;
    private readonly RunDataset? _recording;
    private readonly bool _editable;
    private readonly Dictionary<string, Func<string>> _fields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedSteps = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedChoices = new(StringComparer.OrdinalIgnoreCase);
    private ProcedureEditorItem? _item;
    private bool _updating;
    private bool _closeAccepted;

    public ProcedureEditorWindow(ProcedureEditorSession session, string title, RunDataset? recording, bool writable)
    {
        _session = session;
        _recording = recording;
        _editable = writable && session.CanEdit;
        _updating = true;
        InitializeComponent();
        ProcedureTitle.Text = title;
        ApplyProcedure.IsVisible = _editable;
        ReviewDraft.IsVisible = session.CanEdit;
        DataMode.IsVisible = session.CanEdit;
        ProcedureStatus.Text = session.InspectionReason ?? (writable ? "Edits apply to the draft. Use Save all to publish them." : "This workspace is read-only.");
        _updating = false;
        Closing += OnWindowClosing;
        Refresh();
    }

    private void Refresh(string? select = null)
    {
        _updating = true;
        var showData = DataMode.IsChecked == true;
        var search = ProcedureSearch.Text ?? "";
        var items = _session.Items(showData, search);
        if (!showData && string.IsNullOrWhiteSpace(search) && _collapsedSteps.Count > 0)
        {
            var visible = new List<ProcedureEditorItem>();
            int? collapsedDepth = null;
            foreach (var item in items)
            {
                if (collapsedDepth is { } depth && item.Depth > depth) continue;
                visible.Add(item);
                collapsedDepth = _collapsedSteps.Contains(item.Id) && item.Id != select ? item.Depth : null;
            }
            items = visible;
        }
        ProcedureEntries.ItemsSource = items;
        ProcedureEntries.SelectedItem = items.FirstOrDefault(item => item.Id == (select ?? _item?.Id)) ?? items.FirstOrDefault();
        _updating = false;
        Present(ProcedureEntries.SelectedItem as ProcedureEditorItem);
    }

    private void CommitFields()
    {
        if (_editable && _item is not null && _fields.Count > 0)
            _session.SetFields(_item, _fields.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal));
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (_updating || sender is RadioButton { IsChecked: not true }) return;
        var old = _item;
        if (Attempt(() => { CommitFields(); Refresh(); })) return;
        _updating = true;
        if (old?.IsStep == false) DataMode.IsChecked = true;
        else StepsMode.IsChecked = true;
        _updating = false;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_updating) _ = Attempt(() => { CommitFields(); Refresh(); });
    }

    private void OnEntryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        var selected = ProcedureEntries.SelectedItem as ProcedureEditorItem;
        if (Attempt(() => { CommitFields(); Present(selected); })) return;
        _updating = true;
        ProcedureEntries.SelectedItem = _item;
        _updating = false;
    }

    private void Present(ProcedureEditorItem? item)
    {
        _item = item;
        _fields.Clear();
        _completedChoices.Clear();
        ItemFields.Children.Clear();
        RelatedItems.Children.Clear();
        PromptPreview.IsVisible = false;
        RecordingRows.Text = null;
        ItemTitle.Text = item?.Name ?? "No matching items";
        ItemDetail.Text = item is null ? null : _session.Describe(item);
        var canMoveUp = item is not null && _editable && _session.CanMove(item, -1);
        var canMoveDown = item is not null && _editable && _session.CanMove(item, 1);
        MoveEarlier.IsVisible = canMoveUp;
        MoveLater.IsVisible = canMoveDown;
        PreviewOperator.IsVisible = item is { IsStep: true } && _session.CanPreview(item);
        ToggleChildren.IsVisible = item is { IsStep: true } && string.IsNullOrWhiteSpace(ProcedureSearch.Text) && _session.HasStepChildren(item);
        ToggleChildren.Content = item is not null && _collapsedSteps.Contains(item.Id) ? "Expand children" : "Collapse children";
        StepActions.IsVisible = canMoveUp || canMoveDown || PreviewOperator.IsVisible || ToggleChildren.IsVisible;
        var choices = item is null || !_editable ? [] : _session.AddableChildren(item).Select(schema => new StepChoice(schema)).ToArray();
        AddActions.IsVisible = choices.Length > 0;
        DuplicateActions.IsVisible = item is not null && _editable && _session.CanDuplicateData(item);
        RemoveItem.IsVisible = item is not null && _editable && (_session.CanRemoveStep(item) || _session.CanRemoveData(item));
        RemoveItem.Content = item?.IsStep == true ? "Remove step" : "Remove row";
        NewDataId.Clear();
        ChildType.ItemsSource = choices;
        ChildType.SelectedIndex = choices.Length > 0 ? 0 : -1;
        var related = item is null ? [] : _session.Related(item);
        RelatedHeading.IsVisible = related.Count > 0;
        if (item is null) return;
        foreach (var field in _session.Fields(item)) AddField(field);
        foreach (var target in related)
        {
            var button = new Button { Content = $"{target.Kind} · {target.Name}", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            button.Click += (_, _) => Attempt(() =>
            {
                CommitFields();
                _updating = true;
                ProcedureSearch.Clear();
                if (target.IsStep)
                {
                    StepsMode.IsChecked = true;
                    foreach (var ancestor in _session.AncestorIds(target)) _ = _collapsedSteps.Remove(ancestor);
                }
                else
                {
                    DataMode.IsChecked = true;
                }

                _updating = false;
                Refresh(target.Id);
                _ = ProcedureEntries.Focus();
            });
            RelatedItems.Children.Add(button);
        }
        RecordingRows.Text = _session.RecordedResults(item, _recording);
    }

    private void OnToggleChildren(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        if (_item is not { } item) return;
        if (!_collapsedSteps.Remove(item.Id)) _ = _collapsedSteps.Add(item.Id);
        Refresh();
        _ = ToggleChildren.Focus();
    });

    private void OnMoveUp(object? sender, RoutedEventArgs e) => Move(-1);
    private void OnMoveDown(object? sender, RoutedEventArgs e) => Move(1);
    private void Move(int offset) => Attempt(() =>
    {
        CommitFields();
        if (_item is { } item) { _session.Move(item, offset); Refresh(item.Id); }
    });

    private void OnAddChild(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        if (_item is not { } item || ChildType.SelectedItem is not StepChoice choice) return;
        var id = _session.AddChild(item, choice.Schema);
        _ = _collapsedSteps.Remove(item.Id);
        _updating = true;
        ProcedureSearch.Clear();
        _updating = false;
        Refresh(id);
    });

    private void OnDuplicate(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        if (_item is not { } item) return;
        var id = _session.DuplicateData(item, NewDataId.Text ?? "");
        _updating = true;
        ProcedureSearch.Clear();
        _updating = false;
        Refresh(id);
    });

    private void OnRemove(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        if (_item is not { } item) return;
        if (!Equals(RemoveItem.Content, "Confirm removal"))
        {
            RemoveItem.Content = "Confirm removal";
            ProcedureStatus.Text = $"Remove {item.Name} and any items it contains?";
            return;
        }
        var id = item.IsStep ? _session.RemoveStep(item) : _session.RemoveData(item);
        Refresh(id);
    });

    private void OnReview(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        var pending = _session.ReviewChanges();
        var external = _session.ReviewExternalChanges();
        ProcedureStatus.Text = (pending.Count == 0 ? "No pending field changes." : string.Join("\n", pending))
            + (external.Count == 0 ? "" : "\nChanges on disk compared with this draft\n" + string.Join("\n", external));
    });

    private void OnApply(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        var draft = _session.Complete();
        _closeAccepted = true;
        Close(draft);
    });

    private bool HasLocalEdits() => _editable && (_session.HasChanges || (_item is { } item
        && _session.Fields(item).Any(field => _fields.TryGetValue(field.Definition.Key, out var read) && read() != field.Value)));

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (HasLocalEdits() && !Equals(CloseProcedure.Content, "Discard changes")) { OfferDiscard(); return; }
        _closeAccepted = true;
        Close(null);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeAccepted || !HasLocalEdits()) return;
        e.Cancel = true;
        OfferDiscard();
    }

    private void OfferDiscard()
    {
        CloseProcedure.Content = "Discard changes";
        ProcedureStatus.Text = "This editor has changes. Apply to draft or discard them.";
    }

    private void OnPreview(object? sender, RoutedEventArgs e) => Attempt(() =>
    {
        CommitFields();
        _completedChoices.Clear();
        RenderPrompt();
    });

    private void RenderPrompt()
    {
        if (_item is null) return;
        PromptPreview.Children.Clear();
        PromptPreview.IsVisible = true;
        var prompt = _session.OperatorPrompt(_item, _completedChoices);
        if (prompt is null) { PromptPreview.Children.Add(new TextBlock { Text = "All configurations completed in preview." }); return; }
        PromptPreview.Children.Add(new TextBlock { Text = prompt.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        PromptPreview.Children.Add(new TextBlock { Text = prompt.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var choices = prompt.Fields.SelectMany(field => field.Choices ?? []).ToArray();
        var pick = new ComboBox { ItemsSource = choices, PlaceholderText = "Select a configuration", IsVisible = choices.Length > 0 };
        AutomationProperties.SetName(pick, "Preview fixture configuration");
        PromptPreview.Children.Add(pick);
        var accept = new Button { Content = "Continue preview" };
        accept.Click += (_, _) => Attempt(() =>
        {
            if (choices.Length == 0) { PromptPreview.IsVisible = false; return; }
            if (pick.SelectedItem is not string choice) throw new InvalidDataException("Choose a configuration before continuing.");
            _ = _completedChoices.Add(choice);
            RenderPrompt();
        });
        var cancel = new Button { Content = "End preview" };
        cancel.Click += (_, _) => { _completedChoices.Clear(); PromptPreview.IsVisible = false; };
        var actions = new WrapPanel { ItemSpacing = 8 };
        actions.Children.Add(accept);
        actions.Children.Add(cancel);
        PromptPreview.Children.Add(actions);
        _ = pick.Focus();
    }

    private bool Attempt(Action action)
    {
        try { action(); return true; }
        catch (Exception error) when (error is InvalidDataException or AuthoringWorkspaceException or IOException or InvalidOperationException or System.Text.Json.JsonException)
        { ProcedureStatus.Text = error.Message; return false; }
    }

    private sealed record StepChoice(ProcedureStepEditor Schema) { public override string ToString() => Schema.Label; }
}
