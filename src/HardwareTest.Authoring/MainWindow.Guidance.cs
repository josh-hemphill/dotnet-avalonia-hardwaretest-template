using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private GuidedFormState? _guidedForm;
    private Guid _guidedSession;
    private string? _guidedPlan;
    private bool _guidanceVisible;
    private Guid? _explicitGuidanceSession;
    private bool _guidanceRefreshQueued;
    private bool GuidanceAllowed => !_viewModel.SkipGuidance || _explicitGuidanceSession == _viewModel.WorkspaceSessionId;
    private readonly TextBlock _guidanceFeedback = new() { TextWrapping = TextWrapping.Wrap };

    private void InitializeGuidance()
    {
        AutomationProperties.SetName(_guidanceFeedback, "Guidance feedback");
        AutomationProperties.SetLiveSetting(_guidanceFeedback, AutomationLiveSetting.Polite);
        var actions = new WrapPanel();
        void Action(string name, System.Action action)
        {
            var button = new Button { Content = name, ContentTemplate = AuthoringActionLabels.WrappedText, Margin = new Avalonia.Thickness(4) };
            AutomationProperties.SetName(button, name);
            if (name == "Skip saved guidance") button.IsEnabled = _viewModel.PreferencesEditable;
            button.Click += (_, _) => { if (GuidanceTargetCurrent()) action(); RefreshGuidance(); };
            actions.Children.Add(button);
        }
        Action("Preview voltage result", () => WorkspaceTabs.SelectedIndex = 5);
        Action("Fix guidance blockers", () => WorkspaceTabs.SelectedIndex = 2);
        Action("Prepare guidance packages", () => WorkspaceTabs.SelectedIndex = 3);
        Action("Review guidance build", () => WorkspaceTabs.SelectedIndex = 4);
        Action("Validate saved plans", () => OnValidate(this, new RoutedEventArgs()));
        Action("Save and check draft", () => { CommitFocusedEditor(); TryRun(() => _viewModel.Apply()); });
        Action("Leave saved guidance", () => _guidanceVisible = false);
        Action("Skip saved guidance", () => { _explicitGuidanceSession = null; _viewModel.SkipGuidance = true; _guidanceVisible = false; });
        var host = this.FindControl<Border>("GuidanceHost")!;
        host.Child = new ScrollViewer
        {
            MaxHeight = 100,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new StackPanel { Spacing = 4, Children = { _guidanceFeedback, actions } }
        };
        host.AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
        {
            // Reveal the whole help viewport when its inner scroller reveals an action.
            if (!ReferenceEquals(e.TargetObject, host)) host.BringIntoView();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _viewModel.PropertyChanged += (_, _) => QueueGuidanceRefresh();
    }

    private void QueueGuidanceRefresh()
    {
        if (!_guidanceVisible || _guidanceRefreshQueued || _ownerClosed) return;
        _guidanceRefreshQueued = true;
        Dispatcher.UIThread.Post(() => { _guidanceRefreshQueued = false; RefreshGuidance(); });
    }

    private bool GuidanceTargetCurrent() => !_ownerClosed && IsVisible && !_transitionInFlight && !_destructiveInFlight
        && !_viewModel.OperationBusy && !_viewModel.OperationCleanupPending && ReferenceEquals(DataContext, _viewModel)
        && _guidedSession == _viewModel.WorkspaceSessionId && _viewModel.SelectedProgram?.PlanId == _guidedPlan;

    private void RefreshGuidance()
    {
        var host = this.FindControl<Border>("GuidanceHost")!;
        host.IsVisible = _guidanceVisible && GuidanceAllowed && GuidanceTargetCurrent();
        if (!host.IsVisible || _viewModel.SelectedProgram is not { } draft) return;
        try
        {
            var blockers = _viewModel.EditingIssues.Where(issue => issue.PlanId is null || issue.PlanId == draft.PlanId).ToArray();
            var store = new AuthoringDocumentStore(_viewModel.Workspace!.Root);
            var loaded = store.Load(draft.PlanId);
            if (loaded.IsReadOnly || loaded.Document is not { } source)
                throw new AuthoringWorkspaceException(loaded.Error ?? "Saved authoring source is missing or uses an unsupported schema.");
            var sourceMatches = AuthoringDocumentSnapshot.Capture(draft).ContentEquals(AuthoringDocumentSnapshot.Capture(source.ToDraft()));
            var path = _viewModel.Workspace.TapPlanPaths.SingleOrDefault(file => Path.GetFileNameWithoutExtension(file) == draft.PlanId);
            var complete = false;
            if (source is { RequiresCompilation: false } && path is not null)
            {
                var planHash = AuthoringDocumentStore.ComputeHash(store.ValidatePath(path));
                var sidecarHash = AuthoringDocumentStore.ComputeHash(store.ValidatePath(Path.ChangeExtension(path, ".program.json")));
                complete = sourceMatches && !_viewModel.HasUnsavedChanges && blockers.Length == 0 && planHash is not null && sidecarHash is not null
                    && source.CompiledPlanHash == planHash && source.CompiledSidecarHash == sidecarHash;
            }
            _guidanceFeedback.Text = $"{draft.Sidecar.DisplayName} · {(draft.Instruments.Any(instrument => instrument.TypeId.Contains("Mock", StringComparison.OrdinalIgnoreCase)) ? "Demo — Mock instrument" : "Product — physical instrument selection")}\n"
                + $"Generated identity checks: {draft.Setup.OfType<IdentitySetup>().Count()} · Safe shutdown: {(draft.Cleanup.IncludeSafeShutdown ? string.Join(", ", draft.Cleanup.InstrumentSlots) : "disabled")}\n"
                + (complete ? "First voltage test complete: saved source and compiled plan are ready. Preview with example data or an imported recording; review Build for deployment requirements."
                    : !sourceMatches ? "Guidance is incomplete: saved authoring source differs from the displayed draft. Reopen the workspace to load the saved version, or reconcile your current edits before Save/check."
                    : "Next: preview the voltage result, then Save and check draft. Fix guidance blockers in Issues; prepare missing packages in Environment; review deployment requirements in Build.")
                + (blockers.Length == 0 ? "" : "\n" + string.Join("\n", blockers.Select(issue => issue.Message)))
                + (_viewModel.SavePreviewWarning is { } warning ? "\n" + warning : "");
        }
        catch (Exception error)
        {
            // Presentation must not fail edits/saves or recursively publish another view-model error.
            _guidanceFeedback.Text = "Guidance is incomplete: saved input is unavailable or unsafe. Restore readable authoring source and compiled files inside this workspace, then Resume guidance. Use Issues for recovery and Save/check after repair.\n"
                + AuthoringWorkspaceViewModel.PersistenceError(error);
        }
    }

    private async void OnGuidedStart(object? sender, RoutedEventArgs e)
    {
        try { await ShowGuidedInitializationAsync(); }
        catch (Exception error) { _viewModel.ReportError(AuthoringWorkspaceViewModel.PersistenceError(error)); }
    }

    private async void OnResumeGuidance(object? sender, RoutedEventArgs e)
    {
        if (_transitionInFlight || _destructiveInFlight || _ownerClosed || !IsVisible || _viewModel.OperationBusy || _viewModel.OperationCleanupPending) return;
        try
        {
            _explicitGuidanceSession = _viewModel.WorkspaceSessionId;
            if (_viewModel.SkipGuidance && _viewModel.PreferencesEditable) _viewModel.SkipGuidance = false;
            if (_guidedForm is not null && _guidedSession == _viewModel.WorkspaceSessionId) await ShowGuidedInitializationAsync();
            else if (_viewModel.SelectedProgram is { } selected)
            {
                _guidedSession = _viewModel.WorkspaceSessionId; _guidedPlan = selected.PlanId; _guidanceVisible = true; RefreshGuidance();
            }
            else await ShowGuidedInitializationAsync();
        }
        catch (Exception error) { _viewModel.ReportError(AuthoringWorkspaceViewModel.PersistenceError(error)); }
    }

    private async Task ShowGuidedInitializationAsync()
    {
        if (_transitionInFlight || _destructiveInFlight || _ownerClosed || !IsVisible || !_viewModel.CanInitializePlan) return;
        if (!GuidanceAllowed) { await ShowPlanInitializationAsync(); return; }
        if (_initialization is not null) { _initialization.Activate(); return; }
        if (_guidedSession != _viewModel.WorkspaceSessionId) _guidedForm = null;
        _guidedSession = _viewModel.WorkspaceSessionId;
        CommitFocusedEditor();
        var current = OwnerContext();
        var dialog = new PlanInitializationWindow(_viewModel, guided: true, retained: _guidedForm, ownerIsCurrent: current);
        _initialization = dialog;
        try
        {
            var created = await dialog.ShowDialog<bool>(this);
            if (!current()) return;
            _guidedForm = created ? null : dialog.CaptureGuidedForm();
            if (dialog.SkipGuidanceRequested) _explicitGuidanceSession = null;
            if (created)
            {
                _guidedPlan = _viewModel.SelectedProgram?.PlanId; _guidanceVisible = true;
                WorkspaceTabs.SelectedIndex = 0; RefreshGuidance();
            }
        }
        finally { _initialization = null; }
    }
}
