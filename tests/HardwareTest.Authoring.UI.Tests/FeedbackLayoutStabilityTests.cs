using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class FeedbackLayoutStabilityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saving_and_clearing_feedback_preserve_editor_bounds(bool dark)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectMeasure(0);
        fixture.ViewModel.DisplayName += " edited";
        AuthoringUiFixture.Drain();
        var sequence = fixture.Control<ListBox>("Program sequence");
        var inspector = fixture.Control<ScrollViewer>("Selected step inspector");
        var sequenceBounds = BoundsInWindow(sequence, window);
        var inspectorBounds = BoundsInWindow(inspector, window);

        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.NotEmpty(fixture.ViewModel.SaveAllResults);
        Assert.Equal(sequenceBounds, BoundsInWindow(sequence, window));
        Assert.Equal(inspectorBounds, BoundsInWindow(inspector, window));
        fixture.ViewModel.Open(fixture.WorkspaceRoot);
        fixture.NavigateTask(0);
        fixture.ViewModel.SelectMeasure(0);
        Assert.Empty(fixture.ViewModel.SaveAllResults);
        AuthoringUiFixture.Drain();
        Assert.Equal(sequenceBounds, BoundsInWindow(sequence, window));
        Assert.Equal(inspectorBounds, BoundsInWindow(inspector, window));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collapsing_inspector_sections_preserves_width_and_field_values(bool dark)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectMeasure(0);
        AuthoringUiFixture.Drain();
        var inspector = fixture.Control<ScrollViewer>("Selected step inspector");
        var configure = fixture.Control<Expander>("Configure selected step");
        var advanced = fixture.Control<Expander>("Advanced selected step");
        var field = fixture.Control<ComboBox>("Metric instrument slot");
        var selected = field.SelectedItem;
        var inspectorBounds = BoundsInWindow(inspector, window);
        var width = configure.Bounds.Width;
        configure.IsExpanded = false;
        advanced.IsExpanded = false;
        AuthoringUiFixture.Drain();
        Assert.Equal(width, configure.Bounds.Width);
        Assert.Equal(inspectorBounds, BoundsInWindow(inspector, window));
        configure.IsExpanded = true;
        advanced.IsExpanded = true;
        AuthoringUiFixture.Drain();
        Assert.Equal(width, configure.Bounds.Width);
        Assert.Equal(selected, field.SelectedItem);
        Assert.Equal(inspectorBounds, BoundsInWindow(inspector, window));
        Assert.True(field.Bounds.Width <= configure.Bounds.Width);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData(8)]
    [InlineData(3)]
    public void Saving_from_overview_and_environment_preserves_task_workspace_bounds(int route)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        fixture.OpenRememberedWorkspace();
        fixture.NavigateTask(route);
        fixture.ViewModel.DisplayName += " edited";
        AuthoringUiFixture.Drain();
        var workspace = window.FindControl<TabControl>("WorkspaceTabs")!;
        var selector = fixture.Control<ComboBox>("Selected test plan");
        var workspaceBounds = BoundsInWindow(workspace, window);
        var selectorBounds = BoundsInWindow(selector, window);
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.NotEmpty(fixture.ViewModel.SaveAllResults);
        Assert.Equal(route, workspace.SelectedIndex);
        Assert.Equal(workspaceBounds, BoundsInWindow(workspace, window));
        Assert.Equal(selectorBounds, BoundsInWindow(selector, window));
    }

    private static Rect BoundsInWindow(Control control, Window window)
        => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
}
