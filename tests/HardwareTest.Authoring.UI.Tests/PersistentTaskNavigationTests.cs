using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class PersistentTaskNavigationTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public void Visible_tasks_mark_the_current_route_and_keyboard_returns_to_the_editor(int font)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600); window.FontSize = font;
        fixture.OpenRememberedWorkspace();
        var tasks = window.FindControl<Grid>("PlanNavigationBar")!.Children.OfType<ToggleButton>().ToArray();
        Assert.Equal(7, tasks.Length);
        foreach (var task in tasks)
        {
            if (!task.IsEffectivelyVisible) continue;
            ResponsiveShellTests.Inside(task, window);
            Assert.True(task.IsEffectivelyVisible);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(task)));
            AuthoringUiFixture.Click(task);
            Assert.Equal(int.Parse((string)task.Tag!), window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
            Assert.True(task.IsChecked);
            Assert.Single(tasks, button => button.IsChecked == true);
        }
        fixture.NavigateTask(0);
        fixture.ViewModel.SelectSequence(fixture.ViewModel.SequenceItems.ToList().FindIndex(row => row.Label == "Acquire VDC"));
        AuthoringUiFixture.Drain();
        var sequence = fixture.Control<ListBox>("Program sequence");
        Assert.True(sequence.Focus());
        window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        var field = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        Assert.Contains(field.GetVisualAncestors(), control => control is SelectedStepInspectorView);
        ResponsiveShellTests.Inside(field, window);
        window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.True(sequence.IsFocused);
        fixture.NavigateTask(3);
        window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(0, window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.True(sequence.IsFocused);
    }
}
