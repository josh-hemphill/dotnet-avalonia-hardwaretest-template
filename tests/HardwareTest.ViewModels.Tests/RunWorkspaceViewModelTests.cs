using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

/// Steps / Details / Chart selection plus preparation and interaction overlays.
public sealed class RunWorkspaceViewModelTests
{
    [Fact]
    public void Defaults_to_steps_and_shows_preparation_when_session_blocked()
    {
        var sessionBlocked = true;
        var awaiting = false;
        var hasChart = false;
        var hasStep = false;
        var workspace = Create(
            () => sessionBlocked,
            () => awaiting,
            () => hasChart,
            () => hasStep);

        Assert.Equal(RunWorkspace.Steps, workspace.Selected);
        Assert.True(workspace.ShowPreparation);
        Assert.False(workspace.ShowSteps);
        Assert.False(workspace.ShowModeSwitcher);
        Assert.False(workspace.CanOpenChart);
        Assert.False(workspace.CanOpenDetails);
        Assert.False(workspace.CanReturnToSteps);
    }

    [Fact]
    public void Confirming_session_reveals_steps()
    {
        var sessionBlocked = true;
        var workspace = Create(() => sessionBlocked, () => false, () => false, () => false);
        sessionBlocked = false;
        workspace.Refresh();

        Assert.True(workspace.ShowSteps);
        Assert.True(workspace.ShowModeSwitcher);
        Assert.False(workspace.ShowPreparation);
    }

    [Fact]
    public void Interaction_overlays_chart_then_restores_selection()
    {
        var sessionBlocked = false;
        var awaiting = false;
        var hasChart = true;
        var hasStep = true;
        var workspace = Create(
            () => sessionBlocked,
            () => awaiting,
            () => hasChart,
            () => hasStep);

        workspace.OpenChart();
        Assert.True(workspace.ShowChart);
        Assert.True(workspace.IsChartSelected);

        awaiting = true;
        workspace.Refresh();
        Assert.True(workspace.ShowInteraction);
        Assert.False(workspace.ShowChart);
        Assert.Equal(RunWorkspace.Chart, workspace.Selected);

        awaiting = false;
        workspace.Refresh();
        Assert.True(workspace.ShowChart);
        Assert.False(workspace.ShowInteraction);
        Assert.Equal(RunWorkspace.Chart, workspace.Selected);
    }

    [Fact]
    public void OpenDetails_requires_a_step_selection()
    {
        var hasStep = false;
        var workspace = Create(() => false, () => false, () => false, () => hasStep);
        workspace.OpenDetails();
        Assert.Equal(RunWorkspace.Steps, workspace.Selected);

        hasStep = true;
        workspace.Refresh();
        workspace.OpenDetails();
        Assert.True(workspace.ShowDetails);
        Assert.True(workspace.CanReturnToSteps);
    }

    [Fact]
    public void OpenChart_requires_data_unless_already_on_chart()
    {
        var hasChart = false;
        var workspace = Create(() => false, () => false, () => hasChart, () => false);
        workspace.OpenChart();
        Assert.Equal(RunWorkspace.Steps, workspace.Selected);

        hasChart = true;
        workspace.Refresh();
        workspace.OpenChart();
        Assert.True(workspace.ShowChart);

        hasChart = false;
        workspace.Refresh();
        Assert.True(workspace.CanOpenChart);
        Assert.True(workspace.IsChartSelected);
    }

    [Fact]
    public void ResetToSteps_lands_on_the_step_list()
    {
        var workspace = Create(() => false, () => false, () => true, () => true);
        workspace.OpenChart();
        workspace.ResetToSteps();
        Assert.True(workspace.ShowSteps);
        Assert.Equal(RunWorkspace.Steps, workspace.Selected);
    }

    [Fact]
    public void SetDetailVisible_tracks_the_details_workspace()
    {
        var detailVisible = false;
        var workspace = new RunWorkspaceViewModel(
            () => false,
            () => false,
            () => false,
            () => true,
            visible => detailVisible = visible);

        workspace.OpenDetails();
        Assert.True(detailVisible);
        workspace.OpenSteps();
        Assert.False(detailVisible);
    }

    private static RunWorkspaceViewModel Create(
        Func<bool> sessionBlocked,
        Func<bool> awaiting,
        Func<bool> hasChart,
        Func<bool> hasStep)
        => new(sessionBlocked, awaiting, hasChart, hasStep);
}
