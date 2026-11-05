using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class HumanPlanIdentityWorkflowTests
{
    [AvaloniaTheory]
    [InlineData("Sample", "custom-sample")]
    [InlineData("Board demo", "custom-board-demo")]
    public async Task Reserved_title_in_an_empty_workspace_creates_a_custom_discoverable_plan_identity(string title, string expectedId)
    {
        using var fixture = new AuthoringUiFixture();
        OpenEmptyWorkspace(fixture);
        var window = fixture.Show();
        await fixture.WaitForReadinessAsync();
        Assert.Empty(fixture.ViewModel.Programs);
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        Enter(dialog, fixture.Control<TextBox>("Plan display name", dialog), title);
        Assert.Equal(expectedId, fixture.Control<TextBox>("Stable plan ID", dialog).Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(expectedId, fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Equal(title, fixture.ViewModel.DisplayName);
        // The same handoff policy that previously rejected this late now accepts its identity.
        BenchProgramHandoff.RequireCustomPlanId(fixture.ViewModel.SelectedProgram.PlanId);
        Assert.True(new AuthoringDocumentStore(fixture.ViewModel.Workspace!.Root).Load(expectedId).Exists);
    }

    [AvaloniaFact]
    public async Task Manual_reserved_quick_id_is_rejected_before_creation_and_retains_the_form()
    {
        using var fixture = new AuthoringUiFixture();
        OpenEmptyWorkspace(fixture);
        var window = fixture.Show();
        await fixture.WaitForReadinessAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("New test plan"));
        var dialog = Assert.IsType<QuickPlanCreationWindow>(Assert.Single(window.OwnedWindows));
        Enter(dialog, fixture.Control<TextBox>("Plan display name", dialog), "Retain this title");
        Enter(dialog, fixture.Control<TextBox>("Device family", dialog), "retained-family");
        fixture.Control<Expander>("Plan ID and destination", dialog).IsExpanded = true;
        var id = fixture.Control<TextBox>("Stable plan ID", dialog);
        Enter(dialog, id, " sample ");
        AuthoringUiFixture.Click(fixture.Control<Button>("Create test plan", dialog));
        Assert.Same(dialog, Assert.Single(window.OwnedWindows));
        Assert.Contains("built-in factory", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.True(id.IsFocused);
        Assert.Equal(" sample ", id.Text);
        Assert.Equal("Retain this title", fixture.Control<TextBox>("Plan display name", dialog).Text);
        Assert.Equal("retained-family", fixture.Control<TextBox>("Device family", dialog).Text);
        Assert.Empty(fixture.ViewModel.Programs);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        // Programmatic/imported factory examples remain supported by the lower-level model.
        Assert.Equal("sample", new AuthoringPlanInitializer().Construct(new("sample") { StartingPoint = PlanStartingPoint.Empty }).Draft.PlanId);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_and_guided_forms_generate_custom_ids_and_reject_manual_factory_ids_at_the_naming_stage(bool guided)
    {
        using var fixture = new AuthoringUiFixture();
        OpenEmptyWorkspace(fixture);
        var window = fixture.Show();
        await fixture.WaitForReadinessAsync();
        var dialog = new PlanInitializationWindow(fixture.ViewModel, guided: guided, ownerIsCurrent: () => window.IsVisible);
        _ = dialog.ShowDialog<bool>(window);
        AuthoringUiFixture.Drain();
        Enter(dialog, fixture.Control<TextBox>("Plan display name", dialog), "Sample");
        var id = fixture.Control<TextBox>("Stable plan ID", dialog);
        Assert.Equal("custom-Sample", id.Text);
        Enter(dialog, fixture.Control<TextBox>("Device family", dialog), "retained-family");
        Enter(dialog, id, "STATION-HEALTH");
        AuthoringUiFixture.Click(fixture.Control<Button>("Next", dialog));
        Assert.StartsWith("1 of 6", fixture.Control<TextBlock>("Initialization stage", dialog).Text);
        Assert.Contains("built-in factory", fixture.Control<TextBlock>("Initialization error", dialog).Text);
        Assert.True(id.IsFocused);
        Assert.Equal("Sample", fixture.Control<TextBox>("Plan display name", dialog).Text);
        Assert.Equal("retained-family", fixture.Control<TextBox>("Device family", dialog).Text);
        Assert.Empty(fixture.ViewModel.Programs);
        dialog.Close(false);
        AuthoringUiFixture.Drain();
    }

    private static void OpenEmptyWorkspace(AuthoringUiFixture fixture)
    {
        var root = Path.Combine(fixture.WorkspaceRoot, "empty-human-workspace");
        new AuthoringWorkspaceInitializer().Create(new(root, "Empty human workspace", "Empty human tests") { Template = WorkspaceTemplateKind.Empty });
        fixture.ViewModel.Open(root);
    }

    private static void Enter(Window window, TextBox field, string value)
    {
        field.BringIntoView(); AuthoringUiFixture.Drain();
        Assert.True(field.Focus()); field.SelectAll(); window.KeyTextInput(value); AuthoringUiFixture.Drain();
    }
}
