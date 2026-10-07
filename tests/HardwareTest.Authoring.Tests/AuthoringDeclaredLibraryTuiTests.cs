using System.IO.Compression;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringDeclaredLibraryTuiTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("InstrumentComponents.OpenTap")]
    [InlineData("instrumentcomponents.opentap")]
    public async Task Unrelated_partial_import_cannot_grant_TUI_launch_to_a_workspace_declaring_the_missing_library(string declaration)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root);
        workspace.Manifest.SchemaVersion = 2;
        workspace.Manifest.Package.Name = "Declared TUI prerequisite";
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        workspace.Manifest.Dependencies.Add(new() { Package = declaration, Version = "0.1.1" });
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var archive = Path.Combine(root, "unrelated.TapPackage");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
                writer.Write("<Package Name=\"Unrelated\" Version=\"1.0.0\"><Files><File Path=\"marker.txt\"/></Files></Package>");
            using (var writer = new StreamWriter(zip.CreateEntry("marker.txt").Open())) writer.Write("complete unrelated payload");
        }
        new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive });
        Assert.Contains(OpenTapHomeBootstrapper.ListInstalledPackages(home), package => package.Name == "Unrelated");
        Assert.False(StandaloneVisaReadiness.IsLibraryHome(home));
        // Presence fixture only: refusal must precede launching or loading this payload.
        File.WriteAllText(Path.Combine(home.Root, "FixtureTui.dll"), "TUI prerequisite presence fixture");
        var plan = Path.Combine(root, "sample.TapPlan");
        Assert.True(File.Exists(plan)); Assert.True(File.Exists(Path.Combine(home.Root, "tap.dll")));
        var preferences = new AuthoringPreferencesStore(Path.Combine(root, "preferences.json")); preferences.Load();
        var vm = new AuthoringWorkspaceViewModel(preferences: preferences) { OpenTapHomeOverride = home.Root };
        vm.Open(root);
        Assert.True(vm.RequiresInstrumentLibrary);
        Assert.Contains("requires InstrumentComponents.OpenTap", vm.StandaloneVisaReadinessText);
        var before = Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        var blocker = AuthoringExternalTuiLauncher.Prerequisite(home.Root, plan, vm.RequiresInstrumentLibrary);
        Assert.Contains("requires InstrumentComponents.OpenTap", blocker);
        Assert.Contains("Prepare or import", blocker);
        var started = false;
        var launcher = new AuthoringExternalTuiLauncher { TerminalStarted = _ => started = true };
        var error = await Assert.ThrowsAsync<AuthoringWorkspaceException>(() => launcher.LaunchAsync(home.Root, plan, vm.RequiresInstrumentLibrary));
        Assert.Equal(blocker, error.Message); Assert.False(started);
        var after = Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public void Ordinary_mock_home_retains_ordinary_CLI_selection_without_a_library_requirement()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        var root = AuthoringBuildSnapshotTests.Workspace();
        var workspace = AuthoringWorkspaceLoader.Load(root); workspace.Manifest.SchemaVersion = 2;
        AuthoringWorkspaceLoader.SaveManifest(root, workspace.Manifest);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        File.WriteAllText(Path.Combine(home.Root, "FixtureTui.dll"), "TUI prerequisite presence fixture");
        var plan = Path.Combine(root, "sample.TapPlan");
        var blocker = AuthoringExternalTuiLauncher.Prerequisite(home.Root, plan, requiresInstrumentLibrary: false);
        if (OperatingSystem.IsLinux() && !File.Exists("/usr/bin/xfce4-terminal") && !File.Exists("/usr/bin/xterm"))
            Assert.Contains("xfce4-terminal or xterm", blocker);
        else Assert.Null(blocker);
        var start = AuthoringExternalTuiLauncher.WindowsStartInfo("dotnet", home.Root, plan, requiresInstrumentLibrary: false);
        Assert.Equal(["--roll-forward", "Major", Path.Combine(home.Root, "tap.dll"), "tui", plan], start.ArgumentList);
        Assert.False(StandaloneVisaReadiness.IsLibraryHome(home));
    }
}
