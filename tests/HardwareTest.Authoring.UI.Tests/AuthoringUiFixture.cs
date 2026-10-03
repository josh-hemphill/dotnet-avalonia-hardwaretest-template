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

    public AuthoringUiFixture(bool rememberWorkspace = false)
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
        ViewModel = new AuthoringWorkspaceViewModel(preferences: Preferences);
    }

    public string WorkspaceRoot { get; }
    public AuthoringPreferencesStore Preferences { get; }
    public AuthoringWorkspaceViewModel ViewModel { get; }
    public MainWindow? Window { get; private set; }

    public MainWindow Show(double width = 1280, double height = 800)
    {
        Window = new MainWindow(ViewModel) { Width = width, Height = height };
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

            Window.Close();
            Drain();
        }

        Directory.Delete(_root, recursive: true);
    }
}
