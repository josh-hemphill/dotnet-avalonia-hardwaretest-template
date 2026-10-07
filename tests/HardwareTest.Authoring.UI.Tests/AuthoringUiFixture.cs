using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal sealed class AuthoringUiFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-authoring-ui-" + Guid.NewGuid().ToString("N"));

    public AuthoringUiFixture(bool rememberWorkspace = false, IPlanCompiler? compiler = null)
    {
        WorkspaceRoot = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(WorkspaceRoot);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "workspace")))
        {
            File.Copy(file, Path.Combine(WorkspaceRoot, Path.GetFileName(file)));
        }

        Preferences = new AuthoringPreferencesStore(Path.Combine(_root, "preferences", AuthoringPreferencesStore.FileName));
        Preferences.Load();
        if (rememberWorkspace)
        {
            Preferences.Current.LastWorkspace = WorkspaceRoot;
        }

        Preferences.Save();
        Preferences.Load();
        ViewModel = new AuthoringWorkspaceViewModel(compiler, Preferences);
    }

    public string WorkspaceRoot { get; }
    public AuthoringPreferencesStore Preferences { get; }
    public AuthoringWorkspaceViewModel ViewModel { get; }
    public MainWindow? Window { get; private set; }

    public TestLifecycleInteraction Interaction { get; } = new();
    public TestWorkspacePicker Picker { get; } = new();

    public MainWindow Show(double width = 1280, double height = 800, bool realInteraction = false, IAuthoringWorkspacePicker? packOutputPicker = null, IAuthoringWorkspacePicker? offlinePackagePicker = null)
    {
        Window = new MainWindow(ViewModel, realInteraction ? null : Interaction, Picker, packOutputPicker, offlinePackagePicker) { Width = width, Height = height };
        Window.Show();
        Drain();
        return Window;
    }

    public void OpenRememberedWorkspace()
    {
        Click(Control<Button>("Open last workspace from welcome"));
        Assert.True(ViewModel.HasWorkspace);
        Assert.Null(ViewModel.Error);
        // Legacy workflow fixtures begin in the sequence. Overview navigation has dedicated tests.
        Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 0;
        Drain();
    }

    public void NavigateTask(int route)
    {
        if (route == 8) { Click(Control<Button>("Workspace overview")); return; }
        var trigger = Window!.FindControl<Button>("TaskMenuButton")!;
        var menu = (Flyout)trigger.Flyout!;
        menu.ShowAt(trigger); Drain();
        var action = ((Avalonia.Controls.Control)menu.Content!).GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Tag, route.ToString()));
        Click(action);
    }

    public T Control<T>(string automationName, Avalonia.Controls.Control? root = null) where T : Avalonia.Controls.Control
    {
        var scope = root ?? Window ?? throw new InvalidOperationException("Show the window first.");
        var matches = scope.GetVisualDescendants().OfType<T>()
            .Where(control => AutomationProperties.GetName(control) == automationName).ToArray();
        if (matches.Length == 0 && scope is MainWindow owner)
        {
            var palette = owner.OwnedWindows.SingleOrDefault(child => child.Title == "Add a step");
            if (palette is null && automationName is "Recipe" or "Search sequence palette" or "Insertion point" or "Add recipe")
            {
                Click(Control<Button>("Add step"));
                palette = owner.OwnedWindows.Single(child => child.Title == "Add a step");
            }
            if (palette is not null)
                matches = palette.GetVisualDescendants().OfType<T>().Where(control => AutomationProperties.GetName(control) == automationName).ToArray();
        }
        if (matches.Length == 0 && scope is MainWindow)
        {
            var menus = scope.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name is "LifecycleFocusTarget" or "PlanActionsButton" or "TaskMenuButton" or "InspectorActionsButton").ToArray();
            foreach (var menu in menus)
            {
                var flyout = (Flyout)menu.Flyout!;
                flyout.ShowAt(menu);
                Drain();
                matches = ((Avalonia.Controls.Control)flyout.Content!).GetVisualDescendants().OfType<T>()
                    .Where(control => AutomationProperties.GetName(control) == automationName).ToArray();
                if (matches.Length > 0) break;
                flyout.Hide();
            }
        }
        var result = Assert.Single(matches);
        Drain();
        return result;
    }

    public static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Drain();
    }

    public void Type(TextBox box, string text)
    {
        foreach (var menu in Window!.GetVisualDescendants().OfType<Button>().Where(button => button.Flyout is Flyout))
        {
            var flyout = (Flyout)menu.Flyout!;
            if (flyout.Content is Avalonia.Controls.Control content && content.GetVisualDescendants().Contains(box))
            {
                if (!flyout.IsOpen) flyout.ShowAt(menu);
                Drain();
                break;
            }
        }
        box.BringIntoView();
        Drain();
        Assert.True(box.Focus());
        box.SelectAll();
        TopLevel.GetTopLevel(box)!.KeyTextInput(text);
        Drain();
        // Moving focus also commits bindings whose source trigger is LostFocus.
        var focusTarget = TopLevel.GetTopLevel(box) is Window { Title: "Add a step" } palette
            ? palette.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancel add step")
            : Control<Button>("Save all");
        Assert.True(focusTarget.Focus());
        Drain();
        Assert.Equal(text, box.Text);
    }

    public static void Drain() => Dispatcher.UIThread.RunJobs();

    public void Dispose()
    {
        if (Window is not null)
        {
            foreach (var child in Window.OwnedWindows.ToArray())
            {
                child.Close();
            }

            var pendingChoice = Interaction.Pending;
            var pendingPicker = Picker.Pending;
            Interaction.Pending = null;
            Picker.Pending = null;
            pendingChoice?.TrySetResult(UnsavedChangesChoice.Cancel);
            pendingPicker?.TrySetResult(null);
            Interaction.Choice = UnsavedChangesChoice.Discard;
            var frame = new DispatcherFrame();
            async Task CloseAndStopFrameAsync()
            {
                try { await CloseWindowAsync(); }
                finally { frame.Continue = false; }
            }
            var closing = CloseAndStopFrameAsync();
            if (!closing.IsCompleted) Dispatcher.UIThread.PushFrame(frame);
            closing.GetAwaiter().GetResult();
            Assert.False(Window.IsVisible);
        }

        // Closing invalidates recovery writes, but an obsolete writer can still be releasing
        // its private temporary file. Wait for actual fixture removal without blocking UI jobs.
        var cleanupFrame = new DispatcherFrame();
        async Task RemoveAndStopFrameAsync()
        {
            try { await Task.Run(RemoveFixtureAsync); }
            finally { cleanupFrame.Continue = false; }
        }
        var cleanup = RemoveAndStopFrameAsync();
        if (!cleanup.IsCompleted) Dispatcher.UIThread.PushFrame(cleanupFrame);
        cleanup.GetAwaiter().GetResult();
    }

    private async Task RemoveFixtureAsync()
    {
        await ViewModel.StopRecoveryAsync().ConfigureAwait(false);
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private async Task CloseWindowAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Window!.IsVisible)
        {
            Window.Close();
            Drain();
            foreach (var child in Window.OwnedWindows.ToArray())
            {
                var discard = child.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => Equals(button.Content, "Discard"));
                if (discard is not null) Click(discard);
                else child.Close();
            }
            Drain();
            // Keep pumping UI work until delayed cancellation continuations release the guard.
            if (Window.IsVisible) await Task.Delay(1, timeout.Token);
        }
    }

}

internal sealed class TestLifecycleInteraction : IAuthoringLifecycleInteraction
{
    public UnsavedChangesChoice Choice { get; set; } = UnsavedChangesChoice.Cancel;
    public int Calls { get; private set; }
    public TaskCompletionSource<UnsavedChangesChoice>? Pending { get; set; }
    public IReadOnlyList<DirtyProgramSummary> LastSummary { get; private set; } = [];
    public Task<UnsavedChangesChoice> ChooseAsync(IReadOnlyList<DirtyProgramSummary> dirtyPrograms)
    {
        Calls++;
        LastSummary = dirtyPrograms;
        return Pending?.Task ?? Task.FromResult(Choice);
    }
}
internal sealed class TestWorkspacePicker : IAuthoringWorkspacePicker
{
    public string? Path { get; set; }
    public TaskCompletionSource<string?>? Pending { get; set; }
    public Task<string?> PickAsync() => Pending?.Task ?? Task.FromResult(Path);
}
