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
    public async Task Existing_measure_section_can_be_renamed_and_moved_with_its_tests()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var legacyPath = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var legacy = System.Xml.Linq.XDocument.Load(legacyPath);
        legacy.Root!.Attribute(System.Xml.Linq.XName.Get("measureSections", "urn:hardwaretest:authoring"))?.Remove();
        legacy.Save(legacyPath);
        fixture.Show(1280, 800);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        var measure = Assert.Single(vm.SelectedProgram!.Measure.OfType<SectionNode>(), section => section.Name == "Measure");
        var children = measure.Children.Select(node => node.NodeId).ToArray();
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == measure.NodeId));
        fixture.Type(fixture.Control<TextBox>("Selected step name"), "Power supply");
        AuthoringUiFixture.Click(fixture.Control<Button>("Rename selected step"));
        Assert.Equal("Power supply", Assert.IsType<SectionNode>(vm.SelectedProgram.Measure[0]).Name);
        vm.NewSectionName = "Communications";
        vm.AddSection();
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == measure.NodeId));
        await vm.WaitForMovementAvailabilityAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Move selected step down"));
        var moved = Assert.IsType<SectionNode>(vm.SelectedProgram.Measure[^1]);
        Assert.Equal(measure.NodeId, moved.NodeId);
        Assert.Equal("Power supply", moved.Name);
        Assert.Equal("Test steps › Power supply", vm.InspectorBreadcrumb);
        Assert.Equal(children, moved.Children.Select(node => node.NodeId));
        Assert.Equal(SequenceSection.Setup, vm.SequenceItems[0].Section);
        Assert.Equal(SequenceSection.Cleanup, vm.SequenceItems[^1].Section);
    }

    [AvaloniaFact]
    public async Task Named_section_can_be_added_renamed_and_populated_from_the_editor()
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
        Assert.Equal("Power supply", fixture.Control<TextBox>("Selected step name").Text);
        fixture.Type(fixture.Control<TextBox>("Selected step name"), "Power rails");
        AuthoringUiFixture.Click(fixture.Control<Button>("Rename selected step"));
        Assert.Equal("Power rails", Assert.IsType<SectionNode>(vm.SelectedProgram.Measure.Last()).Name);
        vm.SelectedRecipe = vm.Recipes.Single(recipe => recipe.Id == AuthoringRecipeIds.Acquire);
        await vm.WaitForRecipeAvailabilityAsync();
        AuthoringUiFixture.Click(fixture.Control<Button>("Add recipe"));
        section = Assert.IsType<SectionNode>(vm.SelectedProgram.Measure.Last());
        Assert.Single(section.Children);
        vm.NewSectionName = "Display";
        vm.AddSection();
        AuthoringUiFixture.Drain();
        Assert.Equal("Display", fixture.Control<TextBox>("Selected step name").Text);
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == section.NodeId));
        AuthoringUiFixture.Drain();
        Assert.Equal("Power rails", fixture.Control<TextBox>("Selected step name").Text);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public async Task Existing_steps_can_move_to_sections_and_collapse_with_keyboard_at_small_window_size()
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
        await vm.WaitForMovementAvailabilityAsync();
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
        fixture.NavigateTask(0);
        AuthoringUiFixture.Drain();
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
