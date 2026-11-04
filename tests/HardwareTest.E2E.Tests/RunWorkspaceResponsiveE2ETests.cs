using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class RunWorkspaceResponsiveE2ETests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Compact_run_keeps_primary_actions_and_full_session_inspection_without_losing_overview_preference(int font)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = font;
        var main = shown.Main;
        main.NavigateToPageId("RunTest");
        var run = main.RunTest;
        await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        await E2EHarness.WaitUntilAsync(() => run.StepTree.Hierarchy.Count > 0);
        run.SessionPanel.DutSerialInput = "DUT-" + new string('9', 80);
        run.SessionPanel.OperatorInput = "Technician with a long production identity";
        await run.SessionPanel.ConfirmSessionCommand.ExecuteAsync();
        await E2EHarness.PumpUiAsync();
        Assert.False(run.SessionPanel.SessionBlocked);
        Assert.True(run.Workspace.UserWantsOverview);
        var stepList = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "StepList");
        var row = stepList.GetVisualDescendants().OfType<ListBoxItem>()
            .First(item => item.DataContext is StepListItemViewModel { IsHeader: false });
        var rowModel = Assert.IsType<StepListItemViewModel>(row.DataContext);
        Assert.Equal(rowModel.DisplayName, ControlAutomationPeer.CreatePeerForElement(row).GetName());
        Assert.True(run.IsCompactLayout);
        Assert.False(run.Workspace.ShowOverviewSidebar);
        var header = Assert.Single(window.GetVisualDescendants().OfType<RunHeaderView>());
        var selector = header.GetVisualDescendants().OfType<ComboBox>()
            .Single(control => AutomationProperties.GetName(control) == "Program");
        Inside(selector, window);
        foreach (var name in new[] { "Run", "Clear current run and iterations", "Change session", "Current DUT and technician" })
            Inside(Button(header, name), window);

        var summary = Button(header, "Current DUT and technician");
        summary.Flyout!.ShowAt(summary);
        Dispatcher.UIThread.RunJobs();
        var details = header.FindControl<TextBox>("SessionDetailsBox")!;
        Assert.Contains(run.SessionPanel.SessionSummary, details.Text!);
        Assert.Contains(run.HeroStepName, details.Text!);
        Assert.True(details.IsReadOnly);
        Assert.True(details.IsFocused);
        Assert.Equal(window.FontSize, details.FontSize);
        details.SelectAll();
        Assert.Equal(details.Text, details.SelectedText);
        summary.Flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(run.SessionPanel.SessionBlocked);
        Assert.Equal("DUT-" + new string('9', 80), run.SessionPanel.DutSerialInput);

        window.FontSize = 14;
        window.Width = 1280; window.Height = 800;
        await E2EHarness.PumpUiAsync();
        Assert.True(run.Workspace.UserWantsOverview);
        Assert.False(run.IsCompactLayout);
        Assert.True(run.Workspace.ShowOverviewSidebar);
        window.Width = 900; window.Height = 600;
        await E2EHarness.PumpUiAsync();
        Assert.True(run.Workspace.UserWantsOverview);
        Assert.False(run.Workspace.ShowOverviewSidebar);
    }

    private static Button Button(Control root, string name)
        => root.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);

    private static void Inside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var point = control.TranslatePoint(default, window);
        Assert.NotNull(point);
        Assert.True(new Rect(window.ClientSize).Inflate(.75).Contains(new Rect(point.Value, control.Bounds.Size)),
            $"{control.GetType().Name} {control.Bounds.Size} at {point.Value} must fit {window.ClientSize}.");
    }
}
