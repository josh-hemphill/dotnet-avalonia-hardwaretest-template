using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringHomeReadinessTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Fact]
    public void Correcting_home_removes_only_its_warning_immediately_and_retains_saved_compilation_diagnostic()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Package.Name = "Compilation diagnostics"; AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.CreateDemoProgram("no-limits"); vm.ApplyRecipe(AuthoringRecipeIds.MeanGte); vm.Threshold = string.Empty;
        vm.OpenTapHomeOverride = "invalid\0home"; vm.Apply();
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.Contains("OpenTAP home setting is invalid", vm.SavePreviewWarning!);
        var source = new AuthoringDocumentStore(root).GetDocumentPath("no-limits"); var bytes = File.ReadAllBytes(source);
        vm.OpenTapHomeOverride = Path.Combine(root, "valid-missing-directory");
        Assert.Null(vm.EnvironmentPathError); Assert.True(vm.HasUncompiledSources);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.SavePreviewWarning!);
        Assert.DoesNotContain("OpenTAP home setting is invalid", vm.SavePreviewWarning!);
        Assert.Contains(AuthoringCompileCodes.MissingLimits, vm.Error!);
        Assert.DoesNotContain("OpenTAP home setting is invalid", vm.Error!);
        Assert.Equal(bytes, File.ReadAllBytes(source)); Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Direct_pack_callback_changing_selected_home_keeps_original_checked_identity_and_stale_readiness()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var original = AuthoringBuildSnapshotTests.Home(workspace); var replacement = AuthoringBuildSnapshotTests.Home(workspace);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.OpenTapHomeOverride = original.Root;
        var callbacks = 0;
        vm.Pack(AuthoringBuildSnapshotTests.Temp(), new PackOptions { Home = original, Offline = true, PreflightCompleted = _ => { callbacks++; vm.OpenTapHomeOverride = replacement.Root; } });
        Assert.True(callbacks > 0); Assert.False(vm.HasUnsavedChanges); Assert.Equal(replacement.Root, vm.OpenTapHomeOverride);
        var receipt = Assert.IsType<AuthoringBuildReceipt>(vm.LastBuildReceipt);
        Assert.Contains(receipt.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(receipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
        Assert.Contains(receipt.Inputs, input => input.Path.StartsWith(original.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.DoesNotContain(receipt.Inputs, input => input.Path.StartsWith(replacement.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Contains("needs a new build", vm.BuildReadinessText);
        Assert.Single(vm.BuildHistory); Assert.True(File.Exists(Path.Combine(vm.LastCompletedBuild!.OutputDirectory, AuthoringBuildService.ReceiptFileName)));
    }

    [Theory]
    [InlineData("home", false)]
    [InlineData("home", true)]
    [InlineData("bootstrap", false)]
    [InlineData("bootstrap", true)]
    public void Direct_pack_options_record_actual_home_and_only_matching_rendered_home_is_current(string selection, bool matching)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var rendered = AuthoringBuildSnapshotTests.Home(workspace);
        var actual = matching ? rendered : AuthoringBuildSnapshotTests.Home(workspace);
        var compatibility = AuthoringBuildSnapshotTests.Home(workspace);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.OpenTapHomeOverride = rendered.Root;
        vm.Pack(AuthoringBuildSnapshotTests.Temp(), new PackOptions
        {
            Home = selection == "home" ? actual : null,
            BootstrapHomeDirectory = selection == "bootstrap" ? actual.Root : null,
            TuiHome = compatibility,
            Offline = true
        });
        var receipt = vm.LastBuildReceipt!;
        Assert.Contains(receipt.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(receipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
        Assert.Contains(receipt.Inputs, input => input.Path.StartsWith(actual.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Contains(receipt.Inputs, input => input.Path.StartsWith(compatibility.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Equal(actual.Root, vm.LastCompletedBuild!.SelectedHome);
        Assert.Equal(rendered.Root, vm.OpenTapHomeOverride); Assert.False(vm.HasUnsavedChanges);
        if (matching) Assert.DoesNotContain("needs a new build", vm.BuildReadinessText);
        else Assert.Contains("needs a new build", vm.BuildReadinessText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void File_home_or_file_ancestor_is_unavailable_while_supported_saves_and_prior_receipt_survive(bool ancestor)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(root); vm.SelectProgram("sample"); vm.OpenTapHomeOverride = home.Root;
        vm.DisplayName = "Supported saved source"; Assert.True(vm.SaveAll().Succeeded);
        vm.Pack(AuthoringBuildSnapshotTests.Temp(), new PackOptions { Home = home, Offline = true });
        var receipt = vm.LastBuildReceipt;
        var sourcePath = new AuthoringDocumentStore(root).GetDocumentPath("sample"); var sourceBytes = File.ReadAllBytes(sourcePath);
        var blocker = Path.Combine(root, "file-home"); File.WriteAllText(blocker, "preserve this file");
        var selected = ancestor ? Path.Combine(blocker, "child") : blocker; vm.OpenTapHomeOverride = selected;
        Assert.False(WorkspacePackPlan.TryResolveHomePath(vm.Workspace!, selected, out _, out var error));
        Assert.Contains(selected, error!); Assert.Contains("file", error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(selected, vm.AuthoringHomeText); Assert.NotNull(vm.EnvironmentPathError); Assert.False(vm.CanPack);
        Assert.All(vm.EnvironmentPackages, package => Assert.False(package.Satisfied));
        Assert.Throws<PackPreflightException>(() => vm.Pack(AuthoringBuildSnapshotTests.Temp()));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath)); Assert.Same(receipt, vm.LastBuildReceipt);
        vm.DisplayName = "Dirty supported edit"; Assert.True(vm.HasUnsavedChanges); Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.True(vm.SaveAll().Succeeded); Assert.Contains("OpenTAP home setting", vm.SavePreviewWarning!);
        Assert.Equal("preserve this file", File.ReadAllText(blocker)); Assert.Same(receipt, vm.LastBuildReceipt);
        vm.OpenTapHomeOverride = home.Root; Assert.Null(vm.SavePreviewWarning); Assert.Null(vm.EnvironmentPathError);
    }

    [Fact]
    public void Genuinely_missing_home_directory_stays_eligible_for_preparation_first_build()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
        var path = Path.Combine(root, "missing-parent", "missing-home"); vm.OpenTapHomeOverride = path;
        Assert.True(WorkspacePackPlan.TryResolveHomePath(vm.Workspace!, path, out var resolved, out var error));
        Assert.Equal(path, resolved); Assert.Null(error); Assert.Null(vm.EnvironmentPathError); Assert.Equal(path, vm.AuthoringHomeText); Assert.True(vm.CanPack);
        vm.Pack(AuthoringBuildSnapshotTests.Temp(), new PackOptions { Offline = true });
        Assert.True(Directory.Exists(path));
        Assert.Contains(vm.LastBuildReceipt!.RequiredChecks, check => check.Code == "BUILD_COMPATIBILITY_PASS");
        Assert.Contains(vm.LastBuildReceipt.RequiredChecks, check => check.Code == "BUILD_COMPAT_PROVIDER" && check.Message.Contains("Production in-process TuiCompatChecker"));
    }
}
