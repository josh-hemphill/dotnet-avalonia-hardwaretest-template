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

    public MainWindow Show(double width = 1280, double height = 800, bool realInteraction = false)
    {
        Window = new MainWindow(ViewModel, realInteraction ? null : Interaction, Picker) { Width = width, Height = height };
        Window.Show();
        Drain();
        return Window;
    }

    public void OpenRememberedWorkspace()
    {
        Click(Control<Button>("Open last workspace from welcome"));
        Assert.True(ViewModel.HasWorkspace);
        Assert.Null(ViewModel.Error);
    }

    public T Control<T>(string automationName, Avalonia.Controls.Control? root = null) where T : Avalonia.Controls.Control
        => Assert.Single((root ?? Window ?? throw new InvalidOperationException("Show the window first."))
            .GetVisualDescendants().OfType<T>(),
            control => AutomationProperties.GetName(control) == automationName);

    public static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Drain();
    }

    public void Type(TextBox box, string text)
    {
        box.BringIntoView();
        Drain();
        Assert.True(box.Focus());
        box.SelectAll();
        Window!.KeyTextInput(text);
        Drain();
        // Moving focus also commits bindings whose source trigger is LostFocus.
        Assert.True(Control<Button>("Save sidecar").Focus());
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

        Directory.Delete(_root, recursive: true);
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
