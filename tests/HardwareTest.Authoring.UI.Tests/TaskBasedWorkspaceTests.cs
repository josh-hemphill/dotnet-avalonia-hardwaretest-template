using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class TaskBasedWorkspaceTests
{
    [AvaloniaTheory]
    [InlineData(960, 600, 20)]
    [InlineData(1280, 800, 14)]
    public void Opening_a_workspace_lands_on_the_overview_and_cards_open_focused_tasks(int width, int height, int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height); window.FontSize = font;
        AuthoringUiFixture.Click(fixture.Control<Button>("Open last workspace from welcome"));
        var routes = window.FindControl<TabControl>("WorkspaceTabs")!;
        Assert.Equal(8, routes.SelectedIndex);
        Assert.Equal(AutomationControlType.Pane, ControlAutomationPeer.CreatePeerForElement(routes)!.GetAutomationControlType());
        Assert.IsType<TaskPageHost>(routes);
        Assert.Empty(window.GetVisualDescendants().OfType<TabItem>());
        Assert.Null(window.FindControl<Control>("ProgramsRail"));
        var cards = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("taskCard")).ToArray();
        Assert.Equal(6, cards.Length);
        foreach (var name in new[] { "Sequence", "Instruments", "Operator and reports", "Operator preview", "Environment", "Build and package" })
        {
            var card = fixture.Control<Button>($"Open {name} task");
            card.BringIntoView(); AuthoringUiFixture.Drain();
            ResponsiveShellTests.Inside(card, window);
            AuthoringUiFixture.Click(card);
            Assert.NotEqual(8, routes.SelectedIndex);
            ResponsiveShellTests.Inside(fixture.Control<ComboBox>("Selected test plan"), window);
            fixture.NavigateTask(8);
        }
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void Plan_selection_and_task_navigation_retain_incomplete_drafts_without_writing_saved_files()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("other"); Assert.True(vm.SaveAll().Succeeded);
        vm.SelectProgram("sample"); vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Label == "Mean GTE"));
        var files = Directory.EnumerateFiles(fixture.WorkspaceRoot).ToDictionary(path => path, File.ReadAllBytes);
        fixture.Type(fixture.Control<TextBox>("Threshold"), "incomplete threshold");
        fixture.NavigateTask(7);
        fixture.Type(fixture.Control<TextBox>("Display name"), "Edited sample");
        var selector = fixture.Control<ComboBox>("Selected test plan");
        selector.SelectedItem = vm.ProgramRows.Single(row => row.PlanId == "other"); AuthoringUiFixture.Drain();
        Assert.Equal("other", vm.SelectedProgram!.PlanId);
        fixture.Type(fixture.Control<TextBox>("DUT family"), "Other family");
        selector.SelectedItem = vm.ProgramRows.Single(row => row.PlanId == "sample"); AuthoringUiFixture.Drain();
        Assert.Equal("Edited sample", fixture.Control<TextBox>("Display name").Text);
        fixture.NavigateTask(0);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.Label == "Mean GTE")); AuthoringUiFixture.Drain();
        Assert.Equal("incomplete threshold", fixture.Control<TextBox>("Threshold").Text);
        Assert.Contains("sample", vm.DirtyProgramIds); Assert.Contains("other", vm.DirtyProgramIds);
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [AvaloniaFact]
    public void Operator_task_exposes_prompt_creation_and_inserts_into_setup_through_the_palette()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace(); fixture.NavigateTask(7);
        var before = fixture.ViewModel.SelectedProgram!.Setup.Count;
        var saved = File.ReadAllBytes(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan"));
        AuthoringUiFixture.Click(fixture.Control<Button>("Add operator prompt"));
        Assert.Equal(0, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        var palette = Assert.Single(window.OwnedWindows);
        Assert.Equal(AuthoringRecipeIds.Prompt, fixture.ViewModel.SelectedRecipeId);
        Assert.Single(fixture.Control<ListBox>("Recipe", palette).Items);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(before + 1, fixture.ViewModel.SelectedProgram.Setup.Count);
        Assert.True(fixture.ViewModel.HasPromptSetup);
        Assert.Equal(saved, File.ReadAllBytes(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")));
        Assert.Null(fixture.ViewModel.Error);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Step_palette_cancellation_or_workspace_replacement_cannot_insert_into_another_owner(bool replace)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        var before = AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        var palette = Assert.Single(window.OwnedWindows);
        fixture.Control<TextBox>("Search sequence palette", palette).Text = "Operator Prompt";
        fixture.Control<ListBox>("Recipe", palette).SelectedIndex = 0;
        AuthoringUiFixture.Drain();
        if (replace) fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true);
        else palette.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!)));
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }
}
