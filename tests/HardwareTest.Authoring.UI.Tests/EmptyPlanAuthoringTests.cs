using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class EmptyPlanAuthoringTests
{
    [AvaloniaFact]
    public async Task Empty_flat_plan_does_not_present_the_cleanup_placeholder_as_an_editable_first_step()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.InitializePlan(new("flat-empty"));
        fixture.ViewModel.ReplaceSelected(fixture.ViewModel.SelectedProgram! with { Measure = [] });
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<TextBlock>("Empty step editor guidance").IsEffectivelyVisible);
        Assert.False(fixture.Control<ScrollViewer>("Selected step inspector").IsEffectivelyVisible);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add first sequence step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.False(fixture.Control<TextBlock>("Empty step editor guidance").IsEffectivelyVisible);
        Assert.True(fixture.Control<ScrollViewer>("Selected step inspector").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task First_measurement_keeps_the_default_section_and_an_explicit_recipe_choice()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        fixture.ViewModel.InitializePlan(new("first-measurement"));
        var sectionId = Assert.IsType<SectionNode>(Assert.Single(fixture.ViewModel.SelectedProgram!.Measure)).NodeId;
        fixture.ViewModel.SelectedRecipeId = AuthoringRecipeIds.BandScalar; AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add first sequence step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        Assert.Equal(AuthoringRecipeIds.BandScalar, fixture.ViewModel.SelectedRecipeId);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Empty(window.OwnedWindows);
        var section = Assert.IsType<SectionNode>(Assert.Single(fixture.ViewModel.SelectedProgram.Measure));
        Assert.Equal(sectionId, section.NodeId);
        Assert.IsType<MetricNode>(Assert.Single(section.Children));
        Assert.False(fixture.ViewModel.IsSequenceEmpty);
    }

    [AvaloniaTheory]
    [InlineData(960, 600, 20)]
    [InlineData(1280, 800, 14)]
    public async Task Empty_workspace_has_a_visible_creation_path_and_new_plan_can_be_named_and_populated(int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture();
        var root = Path.Combine(fixture.WorkspaceRoot, "empty");
        new AuthoringWorkspaceInitializer().Create(new(root, "Empty workbench", "Empty.Workbench"));
        var window = fixture.Show(width, height); window.FontSize = font;
        fixture.ViewModel.Open(root); fixture.NavigateTask(8);
        var create = fixture.Control<Button>("Create first test plan from overview");
        Assert.True(create.IsEffectivelyVisible); Assert.True(create.IsEffectivelyEnabled);
        create.BringIntoView(); AuthoringUiFixture.Drain();
        ResponsiveShellTests.Inside(create, window);
        AuthoringUiFixture.Click(create);
        var dialog = Assert.Single(window.OwnedWindows);
        fixture.Control<TextBox>("Plan display name", dialog).Text = "Power checks";
        fixture.Control<TextBox>("Stable plan ID", dialog).Text = "power-checks";
        for (var step = 0; step < 5; step++) AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(0, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.Equal("Power checks", fixture.Control<TextBox>("Sequence plan title").Text);
        Assert.Equal("Power checks", Assert.IsType<AuthoringProgramRow>(fixture.Control<ComboBox>("Selected test plan").SelectedItem).DisplayName);
        fixture.Control<TextBox>("Sequence plan title").Text = ""; AuthoringUiFixture.Drain();
        Assert.Equal("power-checks", fixture.ViewModel.SelectedProgramRow!.DisplayName);
        fixture.Type(fixture.Control<TextBox>("Sequence plan title"), "Power rail acceptance");
        fixture.Type(fixture.Control<TextBox>("Sequence device family"), "Controller board");
        Assert.Equal("Power rail acceptance", fixture.ViewModel.DisplayName);
        Assert.Equal("Controller board", fixture.ViewModel.DutFamily);
        var add = fixture.Control<Button>("Add first sequence step");
        Assert.True(add.IsEffectivelyVisible); Assert.True(add.IsEffectivelyEnabled);
        add.BringIntoView(); AuthoringUiFixture.Drain(); ResponsiveShellTests.Inside(add, window);
        AuthoringUiFixture.Click(add);
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        Assert.Equal("End of matching phase", fixture.ViewModel.InsertionPosition);
        Assert.Equal(AuthoringRecipeIds.Prompt, fixture.ViewModel.SelectedRecipeId);
        Assert.True(fixture.Control<Button>("Add recipe", palette).IsEffectivelyEnabled);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Empty(window.OwnedWindows);
        Assert.IsType<OperatorPromptSetup>(Assert.Single(fixture.ViewModel.SelectedProgram!.Setup));
        Assert.False(fixture.ViewModel.IsSequenceEmpty);
        Assert.True(fixture.Control<ListBox>("Program sequence").IsEffectivelyVisible);
        Assert.Null(fixture.ViewModel.Error);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        palette = Assert.Single(window.OwnedWindows);
        fixture.ViewModel.SelectedRecipeId = AuthoringRecipeIds.BandScalar;
        await fixture.WaitForReadinessAsync();
        Assert.True(fixture.Control<Button>("Add recipe", palette).IsEffectivelyEnabled);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(fixture.ViewModel.SelectedProgram.Measure));
        Assert.True(fixture.ViewModel.SaveAll().Succeeded);
        fixture.ViewModel.Open(root); AuthoringUiFixture.Drain();
        Assert.Equal("Power rail acceptance", fixture.ViewModel.DisplayName);
        Assert.Equal("Controller board", fixture.ViewModel.DutFamily);
        Assert.Single(fixture.ViewModel.SelectedProgram!.Setup);
        Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(fixture.ViewModel.SelectedProgram.Measure));
    }

    [AvaloniaFact]
    public async Task Empty_sequence_creation_and_instrument_routes_remain_available_and_undo_restores_the_starter()
    {
        using var fixture = new AuthoringUiFixture();
        var root = Path.Combine(fixture.WorkspaceRoot, "empty");
        new AuthoringWorkspaceInitializer().Create(new(root, "Empty workbench", "Empty.Workbench"));
        var window = fixture.Show(); fixture.ViewModel.Open(root); fixture.NavigateTask(0);
        var create = fixture.Control<Button>("Create first test plan from sequence");
        Assert.True(create.IsEffectivelyVisible); Assert.True(create.IsEffectivelyEnabled);
        AuthoringUiFixture.Click(create);
        Assert.Single(window.OwnedWindows).Close(false);
        fixture.ViewModel.InitializePlan(new("empty")); AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Configure instruments from empty plan"));
        Assert.Equal(1, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        fixture.NavigateTask(0);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add first sequence step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        fixture.ViewModel.Undo(); AuthoringUiFixture.Drain();
        Assert.True(fixture.ViewModel.IsSequenceEmpty);
        Assert.True(fixture.Control<Button>("Add first sequence step").IsEffectivelyVisible);
        fixture.ViewModel.Redo(); AuthoringUiFixture.Drain();
        Assert.False(fixture.ViewModel.IsSequenceEmpty);
        Assert.Single(fixture.ViewModel.SelectedProgram!.Setup);
    }
}
