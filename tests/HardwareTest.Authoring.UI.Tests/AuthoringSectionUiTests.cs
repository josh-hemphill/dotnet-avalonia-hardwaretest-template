using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringSectionUiTests
{
    [AvaloniaFact]
    public void Named_section_can_be_added_renamed_and_populated_from_the_editor()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(1280, 800);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("device-sections");
        fixture.Type(fixture.Control<TextBox>("New section name"), "Power supply");
        AuthoringUiFixture.Click(fixture.Control<Button>("Add section"));
        var section = Assert.IsType<SectionNode>(vm.SelectedProgram!.Measure.Last());
        Assert.Equal("Power supply", section.Name);
        Assert.True(vm.HasSectionEditor);
        Assert.Equal("Power supply", fixture.Control<TextBox>("Section name").Text);
        fixture.Type(fixture.Control<TextBox>("Section name"), "Power rails");
        AuthoringUiFixture.Click(fixture.Control<Button>("Rename section"));
        Assert.Equal("Power rails", Assert.IsType<SectionNode>(vm.SelectedProgram.Measure.Last()).Name);
        vm.SelectedRecipe = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.Acquire);
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe"));
        section = Assert.IsType<SectionNode>(vm.SelectedProgram.Measure.Last());
        Assert.Single(section.Children);
        vm.NewSectionName = "Display";
        vm.AddSection();
        AuthoringUiFixture.Drain();
        Assert.Equal("Display", fixture.Control<TextBox>("Section name").Text);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == section.NodeId));
        AuthoringUiFixture.Drain();
        Assert.Equal("Power rails", fixture.Control<TextBox>("Section name").Text);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public void Existing_steps_can_move_to_sections_and_collapse_with_keyboard_at_small_window_size()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("group-existing-tests");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        var stepId = vm.SelectedProgram!.Measure.Last().NodeId;
        vm.NewSectionName = "Communications";
        vm.AddSection();
        var sectionId = vm.SelectedSequence!.NodeId;
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == stepId));
        AuthoringUiFixture.Drain();
        var destination = fixture.Control<ComboBox>("Destination section");
        destination.SelectedItem = vm.SectionDestinations.Single(item => item.NodeId == sectionId);
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Move selected item to section"));
        var section = Assert.IsType<SectionNode>(vm.SelectedProgram.Measure.Single(node => node.NodeId == sectionId));
        Assert.Contains(section.Children, child => child.NodeId == stepId);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == sectionId));
        AuthoringUiFixture.Drain();
        var list = fixture.Control<ListBox>("Program sequence");
        var sectionRow = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is SequenceRow row && row.NodeId == sectionId);
        Assert.True(sectionRow.Focus());
        window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Contains(vm.SequenceItems, row => row.NodeId == stepId);
        Assert.True(sectionRow.Focus());
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.DoesNotContain(vm.SequenceItems, row => row.NodeId == stepId);
        Assert.Contains(vm.SequenceItems, row => row.NodeId == sectionId);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Contains(vm.SequenceItems, row => row.NodeId == stepId);
        sectionRow = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => item.DataContext is SequenceRow row && row.NodeId == sectionId);
        var collapse = sectionRow.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible);
        var point = collapse.TranslatePoint(new Point(collapse.Bounds.Width / 2, collapse.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        AuthoringUiFixture.Drain();
        Assert.DoesNotContain(vm.SequenceItems, row => row.NodeId == stepId);
        vm.Undo();
        Assert.Contains(vm.SelectedProgram.Measure, node => node.NodeId == stepId);
        Assert.Null(vm.Error);
    }
}
