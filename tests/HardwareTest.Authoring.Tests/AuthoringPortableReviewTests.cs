using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringPortableReviewTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("pass")]
    [InlineData("fail")]
    [InlineData("nested-repeat")]
    public async Task Prepared_compiled_fixture_opens_with_matching_source_and_backup_hashes(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) directory = directory.Parent;
        var original = Path.Combine(directory!.FullName, "docs", "authoring-manual-review", "fixtures", "package17-compiled", name);
        var root = AuthoringBuildSnapshotTests.Temp();
        foreach (var file in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(root, Path.GetRelativePath(original, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
        }
        var bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var vm = new AuthoringWorkspaceViewModel(preferences: new AuthoringPreferencesStore(Path.Combine(AuthoringBuildSnapshotTests.Temp(), "preferences.json")));
        try
        {
            vm.Open(root);
            Assert.Null(vm.Error); Assert.False(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds);
            Assert.False(vm.HasUnsavedChanges); Assert.Equal("board", Assert.Single(vm.Programs).PlanId);
            var store = new AuthoringDocumentStore(root);
            var primary = Assert.IsType<AuthoringDocumentDto>(store.Load("board").Document);
            var compiledIds = XDocument.Load(Path.Combine(root, "board.TapPlan")).Descendants("TestStep")
                .Select(step => Guid.Parse(step.Attribute("Id")!.Value)).ToHashSet();
            foreach (var source in new[] { store.GetDocumentPath("board"), store.GetDocumentPath("board") + ".bak" })
            {
                var document = Assert.IsType<AuthoringDocumentDto>(store.LoadAtPath(source).Document);
                Assert.False(document.RequiresCompilation);
                Assert.True(AuthoringDocumentSnapshot.Capture(primary.ToDraft()).ContentEquals(AuthoringDocumentSnapshot.Capture(document.ToDraft())), "Backup must retain the compiled source node identities, including embedded raw XML.");
                Assert.All(BoardPreviewBuilder.Build(document.ToDraft()), tile => Assert.Contains(tile.NodeId, compiledIds));
                Assert.Equal(AuthoringDocumentStore.ComputeHash(Path.Combine(root, "board.TapPlan")), document.CompiledPlanHash);
                Assert.Equal(AuthoringDocumentStore.ComputeHash(Path.Combine(root, "board.program.json")), document.CompiledSidecarHash);
            }
            foreach (var file in bytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        }
        finally { await vm.StopRecoveryAsync(); }
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("fail")]
    [InlineData("nested-repeat")]
    public async Task Explicit_backup_restore_reopens_ready_and_binds_supplied_recording(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) directory = directory.Parent;
        var original = Path.Combine(directory!.FullName, "docs", "authoring-manual-review", "fixtures", "package17-compiled", name);
        var originals = Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var root = AuthoringBuildSnapshotTests.Temp();
        foreach (var file in originals)
        {
            var destination = Path.Combine(root, Path.GetRelativePath(original, file.Key));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllBytes(destination, file.Value);
        }
        var store = new AuthoringDocumentStore(root);
        var primary = Assert.IsType<AuthoringDocumentDto>(store.Load("board").Document);
        var recording = RunDatasetCatalog.Load(Path.Combine(root, "run.json")).Run;
        var expected = BoardPreviewBuilder.Build(primary.ToDraft(), recording);
        Assert.NotEmpty(expected); Assert.All(expected, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
        File.Copy(store.GetDocumentPath("board") + ".bak", store.GetDocumentPath("board"), overwrite: true);
        var restoredBytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var vm = new AuthoringWorkspaceViewModel(preferences: new AuthoringPreferencesStore(Path.Combine(AuthoringBuildSnapshotTests.Temp(), "preferences.json")));
        try
        {
            vm.Open(root);
            Assert.Null(vm.Error); Assert.False(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds);
            vm.ImportRecording(Path.Combine(root, "run.json"), "restored-backup");
            Assert.NotNull(vm.SelectedDataset); Assert.False(vm.HasUnsavedChanges);
            var actual = vm.BoardTiles;
            Assert.NotEmpty(actual); Assert.All(actual, tile => Assert.NotEmpty(tile.Preview.CannedSamples));
            Assert.Equal(expected.Count, actual.Count);
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index].NodeId, actual[index].NodeId);
                Assert.Equal(expected[index].Scope, actual[index].Scope);
                Assert.Equal(expected[index].Preview.CannedSamples, actual[index].Preview.CannedSamples);
                Assert.Equal(expected[index].Preview.Passed, actual[index].Preview.Passed);
            }
            foreach (var file in restoredBytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            foreach (var file in originals) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        }
        finally { await vm.StopRecoveryAsync(); }
    }

    [Fact]
    public void Unreadable_TUI_home_reports_actionable_prerequisite_without_throwing()
    {
        if (!OperatingSystem.IsLinux()) return; // Actual Unix directory denial is verified on Linux.
        var root = AuthoringBuildSnapshotTests.Temp();
        File.WriteAllText(Path.Combine(root, "tap.dll"), "fixture");
        var plan = Path.Combine(root, "safe.TapPlan"); File.WriteAllText(plan, "fixture");
        var blocked = Path.Combine(root, "unreadable"); Directory.CreateDirectory(blocked);
        File.SetUnixFileMode(blocked, UnixFileMode.None);
        try { Assert.Contains("Cannot inspect", AuthoringExternalTuiLauncher.Prerequisite(root, plan)); }
        finally { File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
}
