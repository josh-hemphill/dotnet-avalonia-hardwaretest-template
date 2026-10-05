using System.IO.Compression;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringOfflineImportTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
    private static AuthoringOperationCoordinator Coordinator() => new(AuthoringChildProcessRunner.ForExecutable(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll")));

    [Fact]
    public async Task Import_uses_snapshot_owned_archive_and_preserves_installed_in_tree_version()
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        var archive = Path.Combine(root, "offline.TapPackage"); WriteArchive(archive, "HardwareTest Basic", "0.9.0");
        File.WriteAllText(Path.Combine(root, "fixture-wait"), "");
        using var coordinator = Coordinator();
        var operation = coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive);
        await Until(() => File.Exists(Path.Combine(root, "fixture-child.json")));
        File.Delete(archive); File.WriteAllText(archive, "source changed after immutable capture");
        File.WriteAllText(Path.Combine(root, "fixture-release"), "");
        var result = await operation;
        Assert.Equal(home.Root, result.Home);
        Assert.Equal("0.9.0", OpenTapHomeBootstrapper.ListInstalledPackages(home).Single(p => p.Name == "HardwareTest Basic").Version);
        Assert.Equal("snapshot payload", File.ReadAllText(Path.Combine(home.Root, "Packages", "HardwareTest Basic", "payload.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, OpenTapHomeBootstrapper.DefaultHomeRelativePath)));
    }

    [Theory]
    [InlineData("wrong-version")]
    [InlineData("package-name-escape")]
    [InlineData("entry-escape")]
    [InlineData("missing-payload")]
    public async Task Failed_import_preserves_every_selected_home_byte_and_cannot_escape(string invalid)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.Dependencies.Add(new() { Package = "Offline Fixture", Version = "^1.2.0" });
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var home = AuthoringBuildSnapshotTests.Home(workspace); var before = Snapshot(home.Root);
        var archive = Path.Combine(root, "offline.TapPackage");
        WriteArchive(archive, invalid == "package-name-escape" ? "../escaped" : "Offline Fixture",
            invalid == "wrong-version" ? "2.0.0" : "1.3.0", invalid == "entry-escape" ? "../escaped.txt" : invalid == "missing-payload" ? "other.txt" : "payload.txt");
        using var coordinator = Coordinator();
        await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive));
        AssertSnapshot(before, home.Root);
        Assert.False(Directory.Exists(Path.Combine(home.Root, "escaped")));
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_replace_generation_after_real_import_preparation_preserves_selected_home(bool replace)
    {
        var root = AuthoringBuildSnapshotTests.Workspace(); var workspace = AuthoringWorkspaceLoader.Load(root); var home = AuthoringBuildSnapshotTests.Home(workspace);
        var before = Snapshot(home.Root); var archive = Path.Combine(root, "offline.TapPackage"); WriteArchive(archive, "Offline Fixture", "1.3.0");
        File.WriteAllText(Path.Combine(root, "fixture-result-wait"), "");
        using var coordinator = Coordinator();
        var operation = coordinator.RunAsync(AuthoringOperationKind.Bootstrap, root, home: home.Root, offlinePackagePath: archive);
        await Until(() => File.Exists(Path.Combine(root, "fixture-prepared")));
        if (replace) coordinator.ReplaceWorkspace(); else coordinator.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        AssertSnapshot(before, home.Root); Assert.False(coordinator.IsBusy);
        Assert.DoesNotContain(OpenTapHomeBootstrapper.ListInstalledPackages(home), p => p.Name == "Offline Fixture");
    }

    private static Dictionary<string, byte[]> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);
    private static void AssertSnapshot(Dictionary<string, byte[]> expected, string root)
    {
        var actual = Snapshot(root); Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var file in expected) Assert.Equal(file.Value, actual[file.Key]);
    }
    private static void WriteArchive(string path, string name, string version, string entry = "payload.txt")
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("package.xml").Open())) writer.Write($"<Package Name=\"{name}\" Version=\"{version}\"><Files><File Path=\"payload.txt\"/></Files></Package>");
        using var payload = new StreamWriter(archive.CreateEntry(entry).Open()); payload.Write("snapshot payload");
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
