using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringActionableFindingsTests
{
    [AvaloniaFact]
    public void Issues_action_selects_program_node_and_focuses_actual_missing_threshold()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "mean(VDC)";
        vm.Threshold = "";
        var issue = Assert.Single(vm.EditingIssues, item => item.Code == AuthoringCompileCodes.MissingLimits);
        var program = vm.SelectedProgram.PlanId;
        vm.CreateProgram("other");
        var tabs = fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!;
        tabs.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        fixture.Control<Expander>("Editing findings").IsExpanded = true;
        AuthoringUiFixture.Drain();
        var button = Assert.Single(fixture.Window!.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.DataContext, issue) && Equals(button.Content, "Go to field"));
        AuthoringUiFixture.Click(button);
        Assert.Equal(program, vm.SelectedProgram!.PlanId);
        Assert.Equal(issue.NodeId, vm.SelectedSequence!.NodeId);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(fixture.Control<TextBox>("Threshold").IsFocused);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_finding_focus_does_not_follow_a_changed_selection_or_revision(bool edit)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "mean(VDC)";
        vm.Threshold = "";
        var issue = Assert.Single(vm.EditingIssues, item => item.Code == AuthoringCompileCodes.MissingLimits);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram.Measure.Count - 1);
        var otherNode = vm.SelectedSequence!.NodeId;
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<TextBox>("Formula expression").Focus());
        typeof(MainWindow).GetMethod("OnOpenFindingProgram", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(fixture.Window, [new Button { DataContext = issue }, new Avalonia.Interactivity.RoutedEventArgs()]);
        if (edit) vm.FormulaSource = "std(VDC)";
        else vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == otherNode));
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<TextBox>("Formula expression").IsFocused);
        Assert.False(fixture.Control<TextBox>("Threshold").IsFocused);
    }

}
