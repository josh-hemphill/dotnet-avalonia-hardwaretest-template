using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringRemovedDraftStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingNestedIncompleteMetricOrExplorationFormulaPrunesStateAndUndoRestoresIt(bool exploration)
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        vm.CreateProgram("removed-state"); vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var acquire = Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[0]);
        var temporary = new MetricNode(new MetricDraft("temporary", "temporary", "scalar", "V", new LimitSpec(null, null, 2), null,
            new ExpressionAlgorithm([acquire.Metric.ChannelKey], $"mean({acquire.Metric.ChannelKey})")));
        var repeat = new RepeatNode(2, [temporary]);
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [acquire, repeat] });
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == temporary.NodeId));
        if (exploration) vm.FormulaExplorationOnly = true;
        else vm.Threshold = "1e-";
        var before = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        vm.RemoveSelectedSequence();
        Assert.Empty(vm.SelectedProgram!.AuthoringState.IncompleteNumericText);
        Assert.Empty(vm.SelectedProgram.AuthoringState.FormulaIntent);
        vm.Apply();
        Assert.False(vm.HasUncompiledSources);
        vm.Undo();
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        if (exploration) Assert.Equal(FormulaDeploymentIntent.Explore, vm.SelectedProgram!.AuthoringState.FormulaIntent[temporary.NodeId]);
        else Assert.Equal("1e-", vm.SelectedProgram!.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(temporary.NodeId, nameof(vm.Threshold))]);
        vm.Redo(); vm.Apply(); vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root); reopened.SelectProgram("removed-state");
        Assert.Empty(reopened.SelectedProgram!.AuthoringState.IncompleteNumericText);
        Assert.Empty(reopened.SelectedProgram.AuthoringState.FormulaIntent);
        Assert.False(reopened.HasUncompiledSources);
        Assert.DoesNotContain(AuthoringDependencyIndex.Build(reopened.SelectedProgram).Nodes, node => node.NodeId == temporary.NodeId);
        Assert.IsType<RepeatNode>(reopened.SelectedProgram.Measure[1]); reopened.StopRecovery();
    }

    [Fact]
    public void UnwrappingIncompleteRepeatPrunesOnlyRepeatStateAndRetainsChildState()
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        vm.CreateProgram("unwrap-state"); vm.ApplyRecipe(AuthoringRecipeIds.Acquire);
        var metric = Assert.IsType<MetricNode>(vm.SelectedProgram!.Measure[0]);
        var repeat = new RepeatNode(2, [metric]);
        var state = vm.SelectedProgram.AuthoringState.Clone();
        state.IncompleteNumericText[AuthoringDocumentState.FieldKey(repeat.NodeId, nameof(vm.RepeatCount))] = "-";
        state.FormulaIntent[metric.NodeId] = FormulaDeploymentIntent.Deploy;
        vm.ReplaceSelected(vm.SelectedProgram with { Measure = [repeat], AuthoringState = state });
        vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == repeat.NodeId));
        vm.RemoveSelectedSequence();
        Assert.Empty(vm.SelectedProgram!.AuthoringState.IncompleteNumericText);
        Assert.Equal(FormulaDeploymentIntent.Deploy, vm.SelectedProgram.AuthoringState.FormulaIntent[metric.NodeId]);
        Assert.Equal(metric.NodeId, Assert.IsType<MetricNode>(Assert.Single(vm.SelectedProgram.Measure)).NodeId);
        vm.Apply(); Assert.False(vm.HasUncompiledSources);
        vm.Undo(); Assert.Equal("-", vm.SelectedProgram!.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(repeat.NodeId, nameof(vm.RepeatCount))]);
        vm.StopRecovery();
    }

    private static string Workspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        var root = Path.Combine(Path.GetTempPath(), "authoring-remove-state-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap"))) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
