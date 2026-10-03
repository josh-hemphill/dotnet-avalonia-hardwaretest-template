using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringFeedbackTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Many_save_results_and_errors_scroll_without_consuming_the_editor(bool failSaves)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true, compiler: failSaves ? new FailingCompiler() : null);
        var window = fixture.Show(960, 600);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.DisplayName = "Edited sample";
        for (var index = 1; index < 35; index++) fixture.ViewModel.CreateProgram($"feedback-{index:00}");
        fixture.ViewModel.SelectProgram("sample");
        fixture.ViewModel.SelectMeasure(0);
        AuthoringUiFixture.Drain();

        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        var result = fixture.ViewModel.LastSaveAllResult!;
        Assert.Equal(!failSaves, result.Succeeded);
        Assert.Equal(35, failSaves ? result.Failures.Count : result.SavedProgramIds.Count);
        var feedback = fixture.Control<ScrollViewer>("Save all result feedback");
        AssertScrollable(feedback, window);
        AssertUsableEditor(fixture, window);
        ScrollToEndWithWheel(feedback, window);
        var finalResult = Assert.Single(fixture.Control<ItemsControl>("Save all results").GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == fixture.ViewModel.SaveAllResults[^1]);
        AssertVisibleInScrollViewport(finalResult, feedback);

        if (failSaves)
        {
            var errors = fixture.Control<ScrollViewer>("Authoring error feedback");
            AssertScrollable(errors, window);
            ScrollToEndWithWheel(errors, window);
            var errorText = fixture.Control<TextBlock>("Authoring error");
            Assert.EndsWith(fixture.ViewModel.LastSaveAllResult!.Failures[^1].Message, errorText.Text);
            AssertFinalTextLineVisible(errorText, errors);
            // Aggregated errors remain bounded even when the results list is hidden.
            feedback.IsVisible = false;
            AuthoringUiFixture.Drain();
            AssertUsableEditor(fixture, window);
            AssertInsideWindow(errors, window);
            AssertFinalTextLineVisible(errorText, errors);
        }
    }

    [AvaloniaFact]
    public void Many_long_dirty_program_ids_scroll_in_modal_while_all_choices_remain_visible()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600, realInteraction: true);
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.DisplayName = "Unsaved sample";
        for (var index = 1; index < 35; index++) fixture.ViewModel.CreateProgram($"unsaved-{index:00}-{new string('x', 160)}");
        var draft = fixture.ViewModel.SelectedProgram;
        var finalId = fixture.ViewModel.DirtyPrograms[^1].PlanId;
        window.Close();
        AuthoringUiFixture.Drain();
        var dialog = Assert.Single(window.OwnedWindows);
        Assert.True(dialog.ClientSize.Height <= window.ClientSize.Height - 80);
        var programs = fixture.Control<ScrollViewer>("Unsaved program list", dialog);
        AssertScrollable(programs, dialog);
        var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(button =>
            Equals(button.Content, "Cancel") || Equals(button.Content, "Discard") || Equals(button.Content, "Save all")).ToArray();
        Assert.Equal(3, buttons.Length);
        foreach (var button in buttons) AssertInsideWindow(button, dialog);
        var cancel = Assert.Single(buttons, button => Equals(button.Content, "Cancel"));
        Assert.True(cancel.IsDefault);
        ScrollToEndWithWheel(programs, dialog);
        var text = Assert.IsType<TextBlock>(programs.Content);
        Assert.EndsWith(finalId, text.Text);
        AssertFinalTextLineVisible(text, programs);
        foreach (var button in buttons) AssertInsideWindow(button, dialog);
        AuthoringUiFixture.Click(cancel);
        Assert.Empty(window.OwnedWindows);
        Assert.True(window.IsVisible);
        Assert.Equal(35, fixture.ViewModel.DirtyPrograms.Count);
        Assert.Same(draft, fixture.ViewModel.SelectedProgram);
    }

    private static void AssertUsableEditor(AuthoringUiFixture fixture, Window window)
    {
        var programs = fixture.Control<ListBox>("Programs");
        var sequence = fixture.Control<ListBox>("Program sequence");
        AssertInsideWindow(programs, window);
        AssertInsideWindow(sequence, window);
        Assert.True(programs.Bounds.Height >= 40, $"Programs height: {programs.Bounds.Height}");
        Assert.True(sequence.Bounds.Height >= 40, $"Sequence height: {sequence.Bounds.Height}");
        Assert.NotEmpty(sequence.Items);
        Assert.True(fixture.Control<Button>("Save all").Focus());
    }

    private static void AssertScrollable(ScrollViewer viewer, Window window)
    {
        AssertInsideWindow(viewer, window);
        Assert.True(viewer.Viewport.Height > 0);
        Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
    }

    private static void ScrollToEndWithWheel(ScrollViewer viewer, Window window)
    {
        var center = viewer.TranslatePoint(new Point(viewer.Bounds.Width / 2, viewer.Bounds.Height / 2), window);
        Assert.NotNull(center);
        window.MouseWheel(center.Value, new Vector(0, -1000), RawInputModifiers.None);
        AuthoringUiFixture.Drain();
    }

    private static void AssertVisibleInScrollViewport(Control control, ScrollViewer viewer)
    {
        Assert.True(viewer.Offset.Y > 0);
        var origin = control.TranslatePoint(default, viewer);
        Assert.NotNull(origin);
        Assert.True(origin.Value.Y >= -1);
        Assert.True(origin.Value.Y + control.Bounds.Height <= viewer.Bounds.Height + 1);
    }

    private static void AssertFinalTextLineVisible(TextBlock text, ScrollViewer viewer)
    {
        Assert.True(viewer.Offset.Y > 0);
        var origin = text.TranslatePoint(default, viewer);
        Assert.NotNull(origin);
        var bottom = origin.Value.Y + text.Bounds.Height;
        Assert.True(bottom > 0 && bottom <= viewer.Bounds.Height + 1, $"Final text line bottom: {bottom}; viewport: {viewer.Bounds.Height}");
    }

    private static void AssertInsideWindow(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var origin = control.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(origin.Value, control.Bounds.Size)),
            $"{control.GetType().Name} at {origin} ({control.Bounds.Size}) must fit inside {window.ClientSize}.");
    }

    private sealed class FailingCompiler : IPlanCompiler
    {
        private readonly PlanCompiler _inner = new();
        public void Save(ProgramDraft draft, string path) => throw new IOException("Injected save failure: check the destination path and retry after resolving the write problem.");
        public void SaveSidecar(string path, ProgramSidecar sidecar) => throw new IOException("Injected save failure: check the destination path and retry after resolving the write problem.");
        public ProgramDraft Load(string path) => _inner.Load(path);
        public DraftWorkspace LoadAll(AuthoringWorkspace workspace) => _inner.LoadAll(workspace);
    }
}
