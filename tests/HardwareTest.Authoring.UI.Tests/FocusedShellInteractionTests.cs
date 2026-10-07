using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class FocusedShellInteractionTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public void Palette_renders_successive_filters_and_executes_the_displayed_command(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = fontSize;
        fixture.OpenRememberedWorkspace();
        Click(fixture.Control<Button>("Command palette"));
        var palette = Assert.Single(window.OwnedWindows);
        Assert.Equal(fontSize, palette.FontSize);
        AuthoringUiFixture.Drain();
        var search = fixture.Control<TextBox>("Search commands", palette);
        var results = fixture.Control<ListBox>("Authoring commands", palette);
        var run = palette.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Run command"));
        var primary = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
        Filter("Save all", "Save all", primary + "+Shift+S");
        Filter("Undo program", "Undo program edit", primary + "+Z");
        Assert.False(run.IsEnabled);
        search.Text = "no-command-has-this-name";
        AuthoringUiFixture.Drain();
        Assert.Empty(results.Items);
        Assert.False(run.IsEnabled);
        Assert.Contains(palette.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "No matching commands.");
        Filter("Toggle Programs rail", "Toggle Programs rail", "");
        Assert.True(run.IsEnabled);
        var before = fixture.ViewModel.ProgramsRailCollapsed;
        Click(run);
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(!before, fixture.ViewModel.ProgramsRailCollapsed);
        Assert.Null(fixture.ViewModel.Error);

        void Filter(string query, string title, string shortcut)
        {
            search.Text = query;
            AuthoringUiFixture.Drain();
            Assert.Single(results.Items);
            Assert.Equal(0, results.SelectedIndex);
            var row = Assert.IsAssignableFrom<Control>(results.ContainerFromIndex(0));
            var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(texts, text => text.Text == title);
            Assert.Contains(texts, text => text.Text == shortcut);
            foreach (var text in texts.Where(text => text.Text == title || text.Text == shortcut))
            {
                if (!string.IsNullOrEmpty(text.Text)) InsidePopup(text);
            }
            ResponsiveShellTests.Inside(search, palette);
            ResponsiveShellTests.Inside(run, palette);
        }
    }

    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public void Visible_menu_triggers_keep_selectors_open_and_dispatch_leaf_actions(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = fontSize;
        fixture.OpenRememberedWorkspace();
        var workspace = window.FindControl<Button>("LifecycleFocusTarget")!;
        Click(workspace);
        var workspaceFlyout = Assert.IsType<Flyout>(workspace.Flyout);
        Assert.True(workspaceFlyout.IsOpen);
        var menu = Assert.IsAssignableFrom<Control>(workspaceFlyout.Content);
        var viewport = Assert.IsType<ScrollViewer>(menu);
        Assert.True(viewport.Bounds.Height >= 80);
        var sidecar = Named<Button>(menu, "Save sidecar");
        sidecar.BringIntoView();
        AuthoringUiFixture.Drain();
        InsidePopup(sidecar);
        Click(sidecar);
        Assert.False(workspaceFlyout.IsOpen);
        Assert.Null(fixture.ViewModel.Error);
        Click(workspace);
        var settings = Named<Button>(menu, "Open settings");
        settings.BringIntoView();
        AuthoringUiFixture.Drain();
        InsidePopup(settings);
        Assert.Equal(fontSize, sidecar.FontSize);
        if (viewport.Extent.Height > viewport.Viewport.Height) Assert.True(viewport.Offset.Y > 0);
        workspaceFlyout.Hide();

        fixture.ViewModel.SelectMeasure(0);
        var trigger = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SequenceActionsButton");
        Click(trigger);
        var actions = Assert.IsType<Flyout>(trigger.Flyout);
        Assert.True(actions.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(actions.Content);
        var scroll = Assert.IsType<ScrollViewer>(content);
        var recipe = Named<ComboBox>(content, "Recipe");
        recipe.BringIntoView();
        AuthoringUiFixture.Drain();
        InsidePopup(recipe);
        Assert.Equal(fontSize, recipe.FontSize);
        Click(recipe);
        AuthoringUiFixture.Drain();
        Assert.True(actions.IsOpen);
        Assert.True(recipe.IsDropDownOpen);
        var popup = TopLevel.GetTopLevel(recipe)!;
        Assert.True(recipe.Focus());
        popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
        popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.True(actions.IsOpen);
        Assert.False(recipe.IsDropDownOpen);
        Assert.NotNull(fixture.ViewModel.SelectedRecipe);
        var insertion = Named<ComboBox>(content, "Insertion point");
        insertion.BringIntoView();
        AuthoringUiFixture.Drain();
        InsidePopup(insertion);
        Click(insertion);
        Assert.True(actions.IsOpen);
        insertion.IsDropDownOpen = false;
        var rename = Named<TextBox>(content, "Selected step name");
        rename.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(rename.Focus());
        rename.Text = "Menu edited step";
        var accept = Named<Button>(content, "Rename selected step");
        accept.BringIntoView();
        AuthoringUiFixture.Drain();
        InsidePopup(accept);
        Assert.True(scroll.Offset.Y > 0);
        Click(accept);
        Assert.False(actions.IsOpen);
        Assert.Equal("Menu edited step", fixture.ViewModel.SelectedMetric!.Name);
        Assert.Null(fixture.ViewModel.Error);
        ResponsiveShellTests.Inside(fixture.Control<ListBox>("Program sequence"), window);
    }

    [AvaloniaTheory]
    [InlineData(20)]
    [InlineData(24)]
    public void Large_text_welcome_with_recent_offer_keeps_every_action_reachable(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = fontSize;
        AuthoringUiFixture.Drain();
        var viewport = fixture.Control<ScrollViewer>("Workspace welcome viewport");
        ResponsiveShellTests.Inside(viewport, window);
        if (fontSize == 24) Assert.True(viewport.Extent.Height > viewport.Viewport.Height);
        foreach (var name in new[] { "Create workspace from welcome", "Open workspace from welcome", "Open last workspace from welcome" })
        {
            var action = fixture.Control<Button>(name);
            action.BringIntoView();
            AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(action, window);
            ResponsiveActionLabelTests.LabelFits(action, window);
        }
        Click(fixture.Control<Button>("Open last workspace from welcome"));
        Assert.True(fixture.ViewModel.HasWorkspace);
        Assert.Null(fixture.ViewModel.Error);
    }

    private static T Named<T>(Control root, string name) where T : Control
        => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetName(control) == name);

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        AuthoringUiFixture.Drain();
    }

    private static void InsidePopup(Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(default, root)!.Value;
        Assert.True(new Rect(root.ClientSize).Inflate(0.75).Contains(new Rect(point, control.Bounds.Size)), $"{control}: {point}/{control.Bounds} outside {root.ClientSize}");
        foreach (var clip in control.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            var local = control.TranslatePoint(default, clip)!.Value;
            Assert.True(new Rect(clip.Bounds.Size).Inflate(0.75).Contains(new Rect(local, control.Bounds.Size)));
        }
    }
}
