using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringReconcileAndFormulaCliTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainingCleanSourceThenOrdinarySaveReexportsIntendedPlan(bool removePlan)
    {
        var root = Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId;
        var intended = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram);
        var path = vm.Workspace!.TapPlanPaths.Single(p => Path.GetFileNameWithoutExtension(p) == id);
        if (removePlan) File.Delete(path); else File.AppendAllText(path, "\n<!-- external edit -->");
        Assert.False(vm.HasUnsavedChanges);
        Assert.Throws<AuthoringWorkspaceException>(() => vm.Validate());
        vm.ReconcileCompiled(id, useCompiledContent: false);
        Assert.True(vm.HasUncompiledSources);
        Assert.True(new AuthoringDocumentStore(root).Load(id).Document!.RequiresCompilation);
        vm.SaveProgram(id);
        Assert.True(File.Exists(path)); Assert.DoesNotContain("external edit", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(vm.HasUncompiledSources);
        AuthoringSourceExportGuard.EnsureCurrent(vm.Workspace);
        Assert.True(intended.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        vm.StopRecovery();
        var reopened = new AuthoringWorkspaceViewModel(); reopened.Open(root); reopened.SelectProgram(id);
        Assert.False(reopened.HasUncompiledSources); Assert.Empty(reopened.CompiledConflictProgramIds);
        Assert.True(intended.ContentEquals(AuthoringDocumentSnapshot.Capture(reopened.SelectedProgram!))); reopened.StopRecovery();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CliEvaluatesSavedExplorationFormulaInsteadOfOldCompiledPlan(bool sourceOnly)
    {
        var root = Workspace();
        var path = Path.Combine(root, "sample.TapPlan"); var compiledBytes = File.ReadAllBytes(path);
        var program = new PlanCompiler().Load(path);
        var metric = new MetricNode(new MetricDraft("exploration", "exploration", "scalar", "V", null, null,
            new ExpressionAlgorithm(["VDC"], "mean(VDC)+2")));
        var source = AuthoringDocumentDto.FromDraft(program with { Measure = [metric] }, compiledPlanHash: AuthoringDocumentStore.ComputeHash(path),
            compiledSidecarHash: AuthoringDocumentStore.ComputeHash(PlanCompiler.SidecarPath(path)));
        source.State.FormulaIntent[metric.NodeId] = FormulaDeploymentIntent.Explore; source.RequiresCompilation = true;
        var store = new AuthoringDocumentStore(root); store.Save(source);
        if (sourceOnly) { File.Delete(path); File.Delete(PlanCompiler.SidecarPath(path)); }
        var recording = Path.Combine(root, "recordings", "sample", "mean-vdc"); Directory.CreateDirectory(recording);
        File.Copy(Path.Combine(RepoRoot(), "tests", "fixtures", "authoring", "recordings", "sample", "mean-vdc", "run.json"), Path.Combine(recording, "run.json"));
        var output = new StringWriter(); var error = new StringWriter();
        Assert.Equal(0, AuthoringCli.Run(["--eval-formulas", root], output, error));
        Assert.Contains("exploration=4", output.ToString(), StringComparison.Ordinal); Assert.Equal("", error.ToString());
        var edited = source.ToDraft() with { Measure = [metric with { Metric = metric.Metric with { Source = new ExpressionAlgorithm(["VDC"], "mean(VDC)+3") } }] };
        var next = AuthoringDocumentDto.FromDraft(edited); next.RequiresCompilation = true; store.Save(next);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, AuthoringCli.Run(["--eval-formulas", root], output, error)); Assert.Contains("exploration=5", output.ToString(), StringComparison.Ordinal);
        if (sourceOnly) Assert.False(File.Exists(path));
        else Assert.Equal(compiledBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":999}")]
    [InlineData("[]")]
    public void CliRejectsUnknownOrCorruptAuthoringSourceWithoutEvaluatingCompiledFallback(string json)
    {
        var root = Workspace(); var store = new AuthoringDocumentStore(root); var path = store.GetDocumentPath("sample");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, json);
        var bytes = File.ReadAllBytes(path); var output = new StringWriter(); var error = new StringWriter();
        Assert.NotEqual(0, AuthoringCli.Run(["--eval-formulas", root], output, error)); Assert.NotEqual("", error.ToString());
        Assert.DoesNotContain("ok ", output.ToString(), StringComparison.Ordinal); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void FailedRollbackForNonIoErrorReportsActualDurableBackupPaths()
    {
        var root = Workspace(); var fail = false;
        var compiler = new PlanCompiler(null, (temporary, destination) =>
        {
            if (fail && destination.EndsWith(".TapPlan", StringComparison.Ordinal))
            {
                File.Delete(destination); Directory.CreateDirectory(destination); throw new InvalidOperationException("original publication failure");
            }
            File.Move(temporary, destination, true);
        });
        var vm = new AuthoringWorkspaceViewModel(compiler); vm.Open(root); vm.Apply();
        var id = vm.SelectedProgram!.PlanId; vm.DisplayName = "Pending source"; fail = true; vm.Apply();
        Assert.Contains("original publication failure", vm.Error, StringComparison.Ordinal);
        Assert.Contains(".preimage", vm.Error, StringComparison.Ordinal); Assert.DoesNotContain("System.String[]", vm.Error, StringComparison.Ordinal);
        var backups = Directory.EnumerateFiles(root, "*.preimage").ToArray(); Assert.NotEmpty(backups);
        Assert.All(backups, backup => Assert.Contains(backup, vm.Error, StringComparison.Ordinal)); vm.StopRecovery();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dirs.proj"))) dir = dir.Parent;
        return dir!.FullName;
    }
    private static string Workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "authoring-reconcile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "plans", "opentap"))) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        return root;
    }
}
