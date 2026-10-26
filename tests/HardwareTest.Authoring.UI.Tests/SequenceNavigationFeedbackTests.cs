using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class SequenceNavigationFeedbackTests
{
    [AvaloniaFact]
    public async Task Setup_navigation_can_add_the_first_prompt_while_measure_is_selected()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.InitializePlan(new("empty-setup") { Instruments = [] });
        Assert.True(vm.SelectedSequence!.IsSection);
        Assert.Empty(vm.SelectedProgram!.Setup);
        fixture.Control<Button>("Manage Setup section").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await vm.WaitForRecipeAvailabilityAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(vm.CanInsertRecipe, vm.SelectedRecipePrerequisites);
        vm.InsertSelectedRecipe();
        Assert.IsType<OperatorPromptSetup>(Assert.Single(vm.SelectedProgram.Setup));
        Assert.Empty(Assert.IsType<SectionNode>(Assert.Single(vm.SelectedProgram.Measure)).Children);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public async Task Pointer_drag_reorders_independent_steps_as_one_undoable_edit()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1280, 1000);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.CreateDemoProgram("drag-sequence");
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        vm.InsertRecipeAtSectionEnd(AuthoringRecipeIds.Acquire);
        AuthoringUiFixture.Drain();
        var first = vm.SelectedProgram!.Measure[0].NodeId;
        var last = vm.SelectedProgram.Measure[1].NodeId;
        var list = fixture.Control<ListBox>("Program sequence");
        Point Center(Guid id)
        {
            var row = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.DataContext is SequenceRow { NodeId: var node } && node == id);
            return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        }
        var start = Center(last);
        var finish = Center(first);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(finish, RawInputModifiers.LeftMouseButton);
        window.MouseUp(finish, MouseButton.Left);
        AuthoringUiFixture.Drain();
        await vm.SequenceMoveTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(vm.Error);
        Assert.Equal(last, vm.SelectedProgram.Measure[0].NodeId);
        vm.Undo();
        Assert.Equal(first, vm.SelectedProgram.Measure[0].NodeId);
    }

    [AvaloniaFact]
    public async Task Clicking_a_section_transfers_keyboard_focus_for_collapse_and_expand()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1280, 800);
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.NewSectionName = "Power rails";
        vm.AddSection();
        vm.SelectedRecipeId = AuthoringRecipeIds.Acquire;
        await vm.WaitForRecipeAvailabilityAsync();
        await vm.WaitForRecipeAvailabilityAsync();
        vm.InsertSelectedRecipe();
        var childId = vm.SelectedSequence!.NodeId;
        var section = vm.SequenceItems.Single(row => row.IsSection);
        var list = fixture.Control<ListBox>("Program sequence");
        list.ScrollIntoView(section);
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<Button>("Save all").Focus());
        var item = Assert.IsType<ListBoxItem>(list.ContainerFromItem(section));
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        AuthoringUiFixture.Drain();
        Assert.True(item.IsFocused);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        AuthoringUiFixture.Drain();
        Assert.DoesNotContain(vm.SequenceItems, row => row.NodeId == childId);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        AuthoringUiFixture.Drain();
        Assert.Contains(vm.SequenceItems, row => row.NodeId == childId);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public void Back_returns_through_sections_and_section_buttons_prepare_insertion()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1280, 800);
        fixture.OpenRememberedWorkspace();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        var back = fixture.Control<Button>("Back to previous section");
        Assert.True(back.IsEnabled);
        tabs.SelectedIndex = 1;
        tabs.SelectedIndex = 3;
        AuthoringUiFixture.Drain();
        Assert.True(back.IsEnabled);
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, tabs.SelectedIndex);
        AuthoringUiFixture.Drain();
        Assert.True(back.Focus());
        window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.None, null);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(back.IsEnabled);
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(8, tabs.SelectedIndex);
        Assert.False(back.IsEnabled);
        fixture.NavigateTask(0);
        fixture.ViewModel.CreateDemoProgram("section-controls");
        fixture.Control<Button>("Manage Shutdown section").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SequenceSection.Cleanup, fixture.ViewModel.SelectedSequence!.Section);
        Assert.StartsWith("Shutdown ›", fixture.ViewModel.InspectorBreadcrumb);
        Assert.Equal(AuthoringRecipeIds.Shutdown, fixture.ViewModel.SelectedRecipe!.Id);
        Assert.Equal("End of section", fixture.ViewModel.InsertionPosition);
        var testSteps = fixture.Control<Button>("Manage test steps");
        Assert.Equal("Test steps", testSteps.Content);
        testSteps.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("End of test steps", fixture.ViewModel.InsertionPosition);
        Assert.Equal("Test steps outside sections", fixture.ViewModel.SectionDestinations[0].Name);
        fixture.Control<Button>("Manage Setup section").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(AuthoringRecipeIds.Prompt, fixture.ViewModel.SelectedRecipe!.Id);
    }
}
