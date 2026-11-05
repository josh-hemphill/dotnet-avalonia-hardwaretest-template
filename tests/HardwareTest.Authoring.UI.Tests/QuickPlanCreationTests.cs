using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class QuickPlanCreationTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task One_dialog_creates_a_named_draft_and_visible_editor_can_populate_save_and_reopen_it(int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600); window.FontSize = font;
        fixture.OpenRememberedWorkspace();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        var name = fixture.Control<TextBox>("Plan display name", dialog);
        Assert.True(name.IsFocused);
        Enter(dialog, name, "Power rail acceptance");
        Enter(dialog, fixture.Control<TextBox>("Device family", dialog), "Controller board");
        var create = fixture.Control<Button>("Create test plan", dialog);
        ResponsiveShellTests.Inside(create, dialog);
        AuthoringUiFixture.Click(create);
        Assert.Empty(window.OwnedWindows);
        Assert.Equal("power-rail-acceptance", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Equal("Power rail acceptance", fixture.ViewModel.DisplayName);
        Assert.Equal("Controller board", fixture.ViewModel.DutFamily);
        Assert.True(fixture.ViewModel.IsSequenceEmpty);
        Assert.True(fixture.Control<Button>("Add step").IsFocused);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add first sequence step"));
        var palette = Assert.Single(window.OwnedWindows);
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        Assert.Single(fixture.ViewModel.SelectedProgram.Setup.OfType<OperatorPromptSetup>());
        AuthoringUiFixture.Click(fixture.Control<Button>("Add step"));
        palette = Assert.Single(window.OwnedWindows);
        var choices = fixture.Control<ListBox>("Recipe", palette);
        choices.SelectedItem = choices.Items.OfType<AuthoringRecipe>().Single(recipe => recipe.Id == AuthoringRecipeIds.BandScalar);
        AuthoringUiFixture.Drain();
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe", palette));
        AuthoringUiFixture.Click(fixture.Control<Button>("Save all"));
        Assert.True(fixture.ViewModel.LastSaveAllResult!.Succeeded);
        Assert.True(await window.ReopenWorkspaceAsync());
        fixture.Control<ComboBox>("Selected test plan").SelectedItem = fixture.ViewModel.ProgramRows.Single(row => row.PlanId == "power-rail-acceptance");
        fixture.WaitForLoading();
        await fixture.WaitForReadinessAsync();
        Assert.Equal("Power rail acceptance", fixture.ViewModel.DisplayName);
        Assert.Equal("Controller board", fixture.ViewModel.DutFamily);
        Assert.Single(fixture.ViewModel.SelectedProgram!.Setup.OfType<OperatorPromptSetup>());
        Assert.Single(AuthoringRecipeCatalog.EnumerateMetrics(fixture.ViewModel.SelectedProgram.Measure));
    }

    [AvaloniaFact]
    public void Validation_focuses_the_affected_field_and_manual_id_survives_title_changes()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        var name = fixture.Control<TextBox>("Plan display name", dialog);
        AutomationProperties.SetHelpText(name, "Use a descriptive name for operators.");
        var originalNameHelp = AutomationProperties.GetHelpText(name);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.True(name.IsFocused);
        Assert.Contains("title", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.Contains("title", AutomationProperties.GetHelpText(name));
        Enter(dialog, name, "sample");
        Assert.Equal(originalNameHelp, AutomationProperties.GetHelpText(name));
        fixture.Control<Expander>("Plan ID and destination", dialog).IsExpanded = true;
        AuthoringUiFixture.Drain();
        Enter(dialog, fixture.Control<TextBox>("Stable plan ID", dialog), "sample");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        var id = fixture.Control<TextBox>("Stable plan ID", dialog);
        Assert.True(id.IsFocused);
        Assert.Contains("already uses", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.Equal(originalNameHelp, AutomationProperties.GetHelpText(name));
        Assert.Contains("already uses", AutomationProperties.GetHelpText(id));
        Enter(dialog, id, "manual-plan-id");
        Assert.DoesNotContain("already uses", AutomationProperties.GetHelpText(id) ?? "");
        Enter(dialog, name, "Revised title");
        Assert.Equal("manual-plan-id", id.Text);
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.Equal("manual-plan-id", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Equal("Revised title", fixture.ViewModel.DisplayName);
        Assert.Equal("generic", fixture.ViewModel.DutFamily);
    }

    [AvaloniaFact]
    public void Keyboard_cancel_retains_local_input_and_returns_focus_without_publishing_a_plan()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        var original = fixture.ViewModel.Programs.Select(program => program.PlanId).ToArray();
        var trigger = fixture.Control<Button>("Save all"); Assert.True(trigger.Focus());
        window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        Enter(dialog, fixture.Control<TextBox>("Plan display name", dialog), "Retained local title");
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.True(trigger.IsFocused);
        Assert.Equal(original, fixture.ViewModel.Programs.Select(program => program.PlanId));
        window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, null); AuthoringUiFixture.Drain();
        dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        Assert.Equal("Retained local title", fixture.Control<TextBox>("Plan display name", dialog).Text);
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("retained-local-title").Exists);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_quick_creation_cannot_publish_into_changed_or_hidden_owner(bool replace)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(); fixture.OpenRememberedWorkspace();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        Enter(dialog, fixture.Control<TextBox>("Plan display name", dialog), "Do not publish");
        if (replace) fixture.ViewModel.CommitOpen(fixture.ViewModel.PrepareOpen(fixture.WorkspaceRoot), discardUnsavedChanges: true);
        else window.Hide();
        fixture.Control<Button>("Create test plan", dialog).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        AuthoringUiFixture.Drain();
        Assert.False(new AuthoringDocumentStore(fixture.WorkspaceRoot).Load("do-not-publish").Exists);
        Assert.DoesNotContain(fixture.ViewModel.Programs, plan => plan.PlanId == "do-not-publish");
        if (!replace) window.Show();
    }

    private static void Enter(Window window, TextBox field, string value)
    {
        field.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(field.Focus()); field.SelectAll(); window.KeyTextInput(value); AuthoringUiFixture.Drain();
    }
}
