using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private GuidedFormState? _guidedForm;
    private Guid _guidedSession;
    private string? _guidedPlan;
    private bool _guidanceVisible;
    private Guid? _explicitGuidanceSession;
    private bool _guidanceRefreshQueued;
    private Control? _guidanceReturnFocus;
    private Control? _guidanceClose;
    private bool _guidanceWasVisible;
    private bool GuidanceAllowed => !_viewModel.SkipGuidance || _explicitGuidanceSession == _viewModel.WorkspaceSessionId;
    private readonly TextBlock _guidanceFeedback = new() { TextWrapping = TextWrapping.Wrap };

    private void InitializeGuidance()
    {
        AutomationProperties.SetName(_guidanceFeedback, "Guidance feedback");
        AutomationProperties.SetLiveSetting(_guidanceFeedback, AutomationLiveSetting.Polite);
        var checks = new StackPanel { Spacing = 6 };
        var deployment = new StackPanel { Spacing = 6 };
        var preferences = new StackPanel { Spacing = 6 };
        void Action(string name, System.Action action)
        {
            var button = new Button { Content = name, ContentTemplate = AuthoringActionLabels.WrappedText, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            button.Classes.Add("authoringAction");
            AutomationProperties.SetName(button, name);
            if (name == "Skip saved guidance") button.IsEnabled = _viewModel.PreferencesEditable;
            button.Click += (_, _) => { if (GuidanceTargetCurrent()) action(); RefreshGuidance(); };
            (name is "Leave saved guidance" or "Skip saved guidance" ? preferences
                : name is "Fix guidance blockers" or "Validate saved plans" or "Save and check draft" ? checks : deployment).Children.Add(button);
        }
        Action("Preview voltage result", () => WorkspaceTabs.SelectedIndex = 5);
        Action("Fix guidance blockers", () => WorkspaceTabs.SelectedIndex = 2);
        Action("Prepare guidance packages", () => WorkspaceTabs.SelectedIndex = 3);
        Action("Review guidance build", () => WorkspaceTabs.SelectedIndex = 4);
        Action("Validate saved plans", () => OnValidate(this, new RoutedEventArgs()));
        Action("Save and check draft", GuidanceSave);
        Action("Leave saved guidance", () => _guidanceVisible = false);
        Action("Skip saved guidance", () => { _explicitGuidanceSession = null; _viewModel.SkipGuidance = true; _guidanceVisible = false; });
        var host = this.FindControl<Border>("GuidanceHost")!;
        var close = new Button { Content = "Close guidance", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "Close guidance");
        _guidanceClose = close;
        KeyboardNavigation.SetTabNavigation(host, KeyboardNavigationMode.Cycle);
        host.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { _guidanceVisible = false; RefreshGuidance(); args.Handled = true; }
        };
        close.Click += (_, _) => { _guidanceVisible = false; RefreshGuidance(); };
        var viewport = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = GuidanceContent(checks, deployment, preferences)
        };
        AutomationProperties.SetName(viewport, "Guidance viewport");
        var drawer = new DockPanel();
        DockPanel.SetDock(close, Dock.Top);
        drawer.Children.Add(close);
        drawer.Children.Add(viewport);
        host.Child = drawer;
        _viewModel.PropertyChanged += (_, _) => QueueGuidanceRefresh();
    }

    private void QueueGuidanceRefresh()
    {
        if (!_guidanceVisible || _ownerClosed) return;
        if (_guidanceRefreshQueued) return;
        _guidanceRefreshQueued = true;
        Dispatcher.UIThread.Post(() => { _guidanceRefreshQueued = false; RefreshGuidance(forceAssessment: false); });
    }

    private bool GuidanceTargetCurrent() => !_ownerClosed && IsVisible && !_transitionInFlight && !_destructiveInFlight
        && !_viewModel.OperationBusy && !_viewModel.OperationCleanupPending && ReferenceEquals(DataContext, _viewModel)
        && _guidedSession == _viewModel.WorkspaceSessionId && _viewModel.SelectedProgram?.PlanId == _guidedPlan;

    private void RefreshGuidance(bool forceAssessment = true)
    {
        var host = this.FindControl<Border>("GuidanceHost")!;
        host.IsVisible = _guidanceVisible && GuidanceAllowed && GuidanceTargetCurrent();
        if (host.IsVisible != _guidanceWasVisible)
        {
            _guidanceWasVisible = host.IsVisible;
            if (host.IsVisible)
            {
                _guidanceReturnFocus = FocusManager?.GetFocusedElement() as Control;
                Dispatcher.UIThread.Post(() => { if (host.IsEffectivelyVisible) _guidanceClose?.Focus(); });
            }
            else if (IsVisible && !_ownerClosed)
            {
                var target = _guidanceReturnFocus is { IsEffectivelyVisible: true } previous ? previous
                    : this.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => AutomationProperties.GetName(button) == "Resume guidance");
                target?.Focus();
                _guidanceReturnFocus = null;
            }
        }
        if (!host.IsVisible || _viewModel.SelectedProgram is not { } draft)
        {
            _guidanceAssessmentGeneration++;
            _guidanceAssessmentIdentity = null;
            return;
        }
        _guidancePlan.Text = draft.Sidecar.DisplayName;
        _guidanceHardware.Text = draft.Instruments.Count == 0 ? "No instruments configured"
            : draft.Instruments.Any(instrument => instrument.TypeId.Contains("Mock", StringComparison.OrdinalIgnoreCase))
            ? "Demo — Mock instrument" : "Product — physical instrument selection";
        _guidanceIdentity.Text = $"Identity checks: {draft.Setup.OfType<IdentitySetup>().Count()}";
        _guidanceShutdown.Text = $"Safe shutdown: {(draft.Cleanup.IncludeSafeShutdown ? string.Join(", ", draft.Cleanup.InstrumentSlots) : "disabled")}";
        if (forceAssessment) _guidanceAssessmentIdentity = null;
        RequestGuidanceAssessment();
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
                _guidedSession = _viewModel.WorkspaceSessionId; _guidedPlan = selected.PlanId; _guidanceVisible = true;
                _guidanceAssessmentIdentity = null; // Explicit resume also checks external file changes.
                RefreshGuidance();
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
        var dialog = new PlanInitializationWindow(_viewModel, guided: true, retained: _guidedForm, ownerIsCurrent: current) { FontSize = FontSize };
        _initialization = dialog;
        try
        {
            var created = await dialog.ShowDialog<bool>(this);
            if (!current()) return;
            _guidedForm = created ? null : dialog.CaptureGuidedForm();
            if (dialog.SkipGuidanceRequested) _explicitGuidanceSession = null;
            if (dialog.EnvironmentRequested) WorkspaceTabs.SelectedIndex = 3;
            if (created)
            {
                _guidedPlan = _viewModel.SelectedProgram?.PlanId; _guidanceVisible = true;
                WorkspaceTabs.SelectedIndex = 0; RefreshGuidance();
            }
        }
        finally { _initialization = null; }
    }
}
