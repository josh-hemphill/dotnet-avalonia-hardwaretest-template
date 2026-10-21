using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class SequenceNavigationFeedbackTests
{
    [AvaloniaFact]
    public void Pointer_drag_reorders_independent_steps_as_one_undoable_edit()
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
        Assert.Null(vm.Error);
        Assert.Equal(last, vm.SelectedProgram.Measure[0].NodeId);
        vm.Undo();
        Assert.Equal(first, vm.SelectedProgram.Measure[0].NodeId);
    }

    [AvaloniaFact]
    public void Back_returns_through_sections_and_section_buttons_prepare_insertion()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(1280, 800);
        fixture.OpenRememberedWorkspace();
        var tabs = window.FindControl<TabControl>("WorkspaceTabs")!;
        var back = fixture.Control<Button>("Back to previous section");
        Assert.False(back.IsEnabled);
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
        Assert.False(back.IsEnabled);
        fixture.ViewModel.CreateDemoProgram("section-controls");
        fixture.Control<Button>("Manage Cleanup section").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SequenceSection.Cleanup, fixture.ViewModel.SelectedSequence!.Section);
        Assert.Equal(AuthoringRecipeIds.Shutdown, fixture.ViewModel.SelectedRecipe!.Id);
        Assert.Equal("End of section", fixture.ViewModel.InsertionPosition);
        fixture.Control<Button>("Manage Setup section").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(AuthoringRecipeIds.Prompt, fixture.ViewModel.SelectedRecipe!.Id);
    }
}
