using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class ResponsiveActionLabelTests
{
    public static IEnumerable<object[]> ActionCases()
    {
        foreach (var (width, height) in new[] { (960, 600), (1280, 800) })
            foreach (var (font, scale) in new[] { (14, 1d), (20, 1.5) })
                foreach (var busy in new[] { false, true })
                    yield return [width, height, font, scale, busy];
    }

    [AvaloniaTheory]
    [MemberData(nameof(ActionCases))]
    public async Task Bounded_action_text_fits_its_rendered_content_and_keeps_sequence_usable(
        int width, int height, int fontSize, double scaling, bool busy)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scaling);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        Task? operation = null;
        if (busy)
        {
            fixture.ViewModel.ConfigureOperations(AuthoringChildProcessRunner.ForExecutable(
                Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")),
                action => Dispatcher.UIThread.Post(action));
            File.WriteAllText(Path.Combine(fixture.WorkspaceRoot, "fixture-wait"), "");
            operation = fixture.ViewModel.RunOperationAsync(AuthoringOperationKind.Bootstrap);
            await Until(() => File.Exists(Path.Combine(fixture.WorkspaceRoot, "fixture-child.json")));
            Assert.True(fixture.ViewModel.OperationBusy);
        }
        try
        {
            AuthoringUiFixture.Drain();
            foreach (var button in window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Classes.Contains("authoringAction")))
            {
                button.BringIntoView();
                AuthoringUiFixture.Drain();
                LabelFits(button, window);
            }
            var sequence = fixture.Control<ListBox>("Program sequence");
            ResponsiveShellTests.Inside(sequence, window);
            Assert.True(sequence.Bounds.Height >= 80, $"Sequence viewport {sequence.Bounds.Size} must retain room for the selected row.");
            sequence.ScrollIntoView(sequence.SelectedItem!);
            AuthoringUiFixture.Drain();
            var selected = Assert.IsAssignableFrom<Control>(sequence.ContainerFromIndex(sequence.SelectedIndex));
            ResponsiveShellTests.Inside(selected, window);

            var program = fixture.Control<Button>("Remove program");
            var step = fixture.Control<Button>("Remove selected");
            Assert.Equal(fixture.ViewModel.RemoveProgramTitle, program.Content);
            Assert.Equal(fixture.ViewModel.RemoveSelectedTitle, step.Content);
            // Simulate translated string labels without changing model or action identity.
            program.Content = "Delete selected program";
            step.Content = "Delete selected sequence";
            AuthoringUiFixture.Drain();
            LabelFits(program, window);
            LabelFits(step, window);
            Assert.Equal("Remove program", AutomationProperties.GetName(program));
            Assert.Equal("Remove selected", AutomationProperties.GetName(step));
            ResponsiveShellTests.Inside(sequence, window);
            Assert.True(sequence.Bounds.Height >= 80);
            sequence.ScrollIntoView(sequence.SelectedItem!);
            AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(selected, window);
        }
        finally
        {
            if (operation is not null)
            {
                fixture.ViewModel.CancelOperation();
                try { await operation; }
                catch (OperationCanceledException) { }
                AuthoringUiFixture.Drain();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(960, 600, 14, 1)]
    [InlineData(960, 600, 20, 1.5)]
    [InlineData(1280, 800, 14, 1)]
    [InlineData(1280, 800, 20, 1.5)]
    public void Seven_workspace_route_headers_fit_one_row_and_remain_readable_and_reachable(
        int width, int height, int fontSize, double scaling)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        window.FontSize = fontSize;
        window.SetRenderScaling(scaling);
        fixture.OpenRememberedWorkspace();
        AuthoringUiFixture.Drain();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        var headers = tabs.Items.Cast<TabItem>().ToArray();
        Assert.Equal(new[] { "Program", "Hardware", "Issues", "Environment", "Build", "Preview", "Definitions" },
            headers.Select(header => header.Header));
        var first = headers[0].TranslatePoint(default, window)!.Value;
        foreach (var header in headers)
        {
            ResponsiveShellTests.Inside(header, window);
            var origin = header.TranslatePoint(default, window)!.Value;
            Assert.Equal(first.Y, origin.Y, precision: 3);
            Assert.True(header.Bounds.Height >= 48, $"Route {header.Header} retains its vertical hit target.");
            var label = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Text, header.Header));
            ResponsiveShellTests.Inside(label, window);
            Assert.DoesNotContain(label.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.True(label.TextLayout.Width <= label.Bounds.Width + 0.75);
            Assert.True(label.TextLayout.Height <= label.Bounds.Height + 0.75);
            var center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            AuthoringUiFixture.Drain();
            Assert.Same(header, tabs.SelectedItem);
        }
        Assert.True(Assert.Single(window.GetVisualDescendants().OfType<WorkspaceDefinitionsView>()).IsEffectivelyVisible);
    }

    private static void LabelFits(Button button, Window window)
    {
        ResponsiveShellTests.Inside(button, window);
        var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Text, button.Content));
        var presenter = Assert.Single(label.GetVisualAncestors().OfType<ContentPresenter>(),
            control => ReferenceEquals(control.Content, button.Content));
        ResponsiveShellTests.Inside(label, window);
        var location = label.TranslatePoint(default, presenter);
        Assert.NotNull(location);
        Assert.True(new Rect(presenter.Bounds.Size).Inflate(0.75).Contains(new Rect(location.Value, label.Bounds.Size)),
            $"Label {label.Text} must fit its actual content presenter {presenter.Bounds.Size}.");
        Assert.DoesNotContain(label.TextLayout.TextLines, line => line.HasCollapsed);
        // Wrapped line-ending spaces have advance width but paint no glyphs. Measure the visible text extent.
        Assert.True(label.TextLayout.Width <= label.Bounds.Width + 0.75,
            $"Rendered label {label.Text}: {label.TextLayout.Width} exceeds {label.Bounds.Width}.");
        Assert.True(label.TextLayout.Height <= label.Bounds.Height + 0.75);
    }

    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Action-label operation did not reach the waiting child.");
            await Task.Delay(20);
            AuthoringUiFixture.Drain();
        }
        AuthoringUiFixture.Drain();
    }
}
