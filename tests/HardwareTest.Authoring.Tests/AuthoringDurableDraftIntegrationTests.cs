using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringDurableDraftIntegrationTests
{
    [Fact]
    public void InvalidNumericAndExplorationFormulaSaveReopenAndBlockCompiledChecks()
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.ApplyRecipe(AuthoringRecipeIds.MeanGte);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        var nodeId = vm.SelectedSequence!.NodeId;
        vm.Threshold = "1e-";
        Assert.Equal("1e-", vm.Threshold);
        Assert.True(vm.HasUnsavedChanges);
        vm.SaveProgram(vm.SelectedProgram.PlanId);
        Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.HasUncompiledSources);
        Assert.False(vm.CanPack);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel();
        reopened.Open(root);
        reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == nodeId));
        Assert.Equal("1e-", reopened.Threshold);
        Assert.True(reopened.HasUncompiledSources);
        reopened.StopRecovery();
    }

    [Fact]
    public void ExternalCompiledEditNeedsExplicitReconciliationBeforeExport()
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(root);
        vm.DisplayName = "Source name";
        vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var planPath = vm.Workspace!.TapPlanPaths.Single(path => Path.GetFileNameWithoutExtension(path) == id);
        File.AppendAllText(planPath, "\n<!-- external -->");
        var external = File.ReadAllBytes(planPath);
        vm.DisplayName = "Changed source";
        vm.Apply();
        Assert.Contains(id, vm.CompiledConflictProgramIds);
        Assert.Equal(external, File.ReadAllBytes(planPath));
        vm.ReconcileCompiled(id, useCompiledContent: false);
        vm.Apply();
        Assert.Empty(vm.CompiledConflictProgramIds);
        Assert.False(vm.HasUncompiledSources);
        vm.StopRecovery();
    }

    [Fact]
    public void NumericDraftHistoryRetainsTypedValueAndUndoRestoresSavedBaseline()
    {
        var draft = AuthoringRecipeCatalog.CreateProgram("draft");
        var session = new AuthoringDocumentSession(draft);
        var state = draft.AuthoringState.Clone();
        state.IncompleteNumericText[AuthoringDocumentState.FieldKey(draft.Cleanup.NodeId, "Count")] = "-";
        session.ApplyEdit("Incomplete text", d => d with { AuthoringState = state });
        Assert.True(session.IsDirty);
        session.Undo();
        Assert.False(session.IsDirty);
        session.Redo();
        Assert.Equal("-", session.Draft.AuthoringState.IncompleteNumericText.Values.Single());
    }

    [Fact]
    public void ExplorationFormulaIntentReopensWithoutCompiledRevision()
    {
        var root = Workspace();
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        vm.ApplyRecipe(AuthoringRecipeIds.Formula);
        vm.SelectMeasure(vm.SelectedProgram!.Measure.Count - 1);
        vm.FormulaSource = "input + 1";
        vm.FormulaExplorationOnly = true;
        var id = vm.SelectedProgram.PlanId;
        var nodeId = vm.SelectedSequence!.NodeId;
        vm.Apply(); vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root); reopened.SelectProgram(id);
        reopened.SelectSequence(reopened.SequenceItems.ToList().FindIndex(row => row.NodeId == nodeId));
        Assert.Equal("input + 1", reopened.FormulaSource);
        Assert.True(reopened.FormulaExplorationOnly);
        Assert.False(reopened.CanPack); reopened.StopRecovery();
    }

    [Fact]
    public void RecoveryOnlyNewProgramRequiresExplicitAcceptanceAndRemainsUnsaved()
    {
        var root = Workspace();
        var draft = AuthoringRecipeCatalog.CreateProgram("unsaved-new");
        var store = new AuthoringDocumentStore(root);
        store.SaveAtPath(store.GetRecoveryPath(draft.PlanId), AuthoringDocumentDto.FromDraft(draft, 9));
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        Assert.DoesNotContain(vm.Programs, p => p.PlanId == draft.PlanId);
        Assert.Contains(draft.PlanId, vm.RecoverableProgramIds);
        vm.AcceptRecovery(draft.PlanId);
        Assert.Equal(draft.PlanId, vm.SelectedProgram!.PlanId);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(9, vm.SelectedDocument!.Revision);
        vm.StopRecovery();
    }

    [Fact]
    public void FutureSourceOpensReadOnlyAndCannotBeReplaced()
    {
        var root = Workspace();
        var store = new AuthoringDocumentStore(root);
        var path = store.GetDocumentPath("future"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"futureValue\":true}");
        File.WriteAllBytes(path, bytes);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        Assert.True(vm.Workspace!.IsReadOnly);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Apply());
        Assert.Equal(bytes, File.ReadAllBytes(path)); vm.StopRecovery();
    }

    private static string Workspace()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        var root = Path.Combine(Path.GetTempPath(), "authoring-durable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "plans", "opentap")))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
