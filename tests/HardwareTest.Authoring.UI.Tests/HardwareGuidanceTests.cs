using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class HardwareGuidanceTests
{
    [AvaloniaFact]
    public void Binding_editor_explains_demo_and_unknown_types_and_links_to_package_recovery()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        tabs.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Load selected binding"));
        var help = fixture.Control<TextBlock>("Selected adapter guidance");
        Assert.Contains("Demo instrument", help.Text);
        Assert.Contains("HardwareTest Basic", help.Text);

        fixture.ViewModel.HardwareEditType = null;
        AuthoringUiFixture.Drain();
        Assert.Contains("Imported types are preserved", help.Text);
        Assert.Contains("Open Environment", help.Text);
        var environment = window.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, "Resolve types and packages in Environment"));
        environment.BringIntoView();
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(environment);
        Assert.Equal(3, tabs.SelectedIndex);
        Assert.NotNull(fixture.Control<StackPanel>("Package sourcing guidance"));
    }

    [AvaloniaFact]
    public void Deferring_an_address_explains_hardware_free_draft_without_mutating_program()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        var original = fixture.ViewModel.SelectedProgram;
        var guidance = fixture.Control<Expander>("Deferred instrument address guidance");
        guidance.IsExpanded = true;
        AuthoringUiFixture.Drain();
        var text = string.Join(" ", guidance.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.Contains("Save all preserves your draft", text);
        Assert.Contains("does not create unresolved hardware slots", text);
        Assert.Same(original, fixture.ViewModel.SelectedProgram);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }
}
