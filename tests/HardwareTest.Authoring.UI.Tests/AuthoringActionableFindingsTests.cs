using System.Reflection;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HardwareTest.OpenTap.Host;
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
    [AvaloniaFact]
    public void Ordinary_editing_issue_opens_its_node_without_a_field_error()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.SelectMeasure(0);
        var channel = vm.ChannelKey;
        vm.SelectMeasure(1);
        vm.ChannelKey = channel;
        var node = vm.SelectedSequence!.NodeId;
        var issue = Assert.Single(vm.EditingIssues, item => item.Code == "DUPLICATE_CHANNEL" && item.NodeId == node);
        Assert.Null(issue.Section);
        Assert.Null(issue.Field);
        Assert.Equal("Open location", issue.NavigationLabel);
        var program = vm.SelectedProgram!.PlanId;
        vm.CreateProgram("other");
        var tabs = fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!;
        tabs.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        fixture.Control<Expander>("Editing findings").IsExpanded = true;
        AuthoringUiFixture.Drain();
        var button = Assert.Single(fixture.Window!.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.DataContext, issue) && Equals(button.Content, "Open location"));
        AuthoringUiFixture.Click(button);
        Assert.Equal(program, vm.SelectedProgram!.PlanId);
        Assert.Equal(node, vm.SelectedSequence!.NodeId);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Null(vm.Error);
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

    [AvaloniaFact]
    public void Section_only_contract_action_opens_configure_without_a_field_error()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var path = Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan");
        var xml = XDocument.Load(path);
        var acquisition = xml.Descendants("TestStep").Single(step => ((string?)step.Attribute("type"))?.EndsWith("AcquireVoltageStep", StringComparison.Ordinal) == true);
        acquisition.Add(new XElement("SeriesCompliance", "allSamples"));
        xml.Save(path);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.Validate();
        var row = Assert.Single(vm.FindingRows, item => item.Code == PlanContractValidator.Codes.ComplianceWithoutLimits);
        Assert.Equal("Open step section", row.NavigationLabel);
        Assert.Null(row.Finding.Target!.Field);
        fixture.Control<Expander>("Configure selected step").IsExpanded = false;
        var tabs = fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!;
        tabs.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        var button = Assert.Single(fixture.Window!.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.DataContext, row) && Equals(button.Content, "Open step section"));
        AuthoringUiFixture.Click(button);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Equal(row.NodeId, vm.SelectedSequence!.NodeId);
        Assert.True(fixture.Control<Expander>("Configure selected step").IsExpanded);
        Assert.Null(vm.Error);
    }

    [AvaloniaTheory]
    [InlineData("Threshold", "Advanced", "Configure selected step", "Threshold")]
    [InlineData("ChannelKey", "Configure", "Advanced selected step", "Channel key")]
    [InlineData(null, "Advanced", "Advanced selected step", null)]
    public void Supported_destinations_open_the_actual_section_and_field(string? field, string requested, string sectionName, string? fieldName)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "mean(VDC)";
        AuthoringUiFixture.Drain();
        fixture.Control<Expander>(sectionName).IsExpanded = false;
        var destination = AuthoringFindingNavigation.Resolve(vm.SelectedProgram, vm.SelectedSequence!.NodeId,
            new(Field: field, Section: requested));
        var inspector = Assert.Single(fixture.Window!.GetVisualDescendants().OfType<SelectedStepInspectorView>());
        Assert.True(inspector.FocusFinding(destination.Target));
        AuthoringUiFixture.Drain();
        Assert.True(fixture.Control<Expander>(sectionName).IsExpanded);
        if (fieldName is not null)
        {
            if (field == "Threshold") Assert.True(fixture.Control<TextBox>(fieldName).IsFocused);
            else Assert.True(Assert.Single(fixture.Control<AutoCompleteBox>(fieldName).GetVisualDescendants().OfType<TextBox>()).IsFocused);
        }
        Assert.Null(vm.Error);
    }

}
