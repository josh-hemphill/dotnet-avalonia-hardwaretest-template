using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringDeclaredLibraryTuiWindowTests
{
    [AvaloniaFact]
    public async Task Declared_library_partial_import_shows_missing_provider_and_refuses_the_actual_TUI_command()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) Assert.Skip("Production launcher supports Linux and Windows.");
        using var fixture = new AuthoringUiFixture();
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.SchemaVersion = 2; workspace.Manifest.Package.Name = "Declared TUI requirement";
        var home = new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = Path.Combine(workspace.Root, "selected-home"), Offline = true });
        workspace.Manifest.Dependencies.Add(new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "0.1.1" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var archive = Path.Combine(workspace.Root, "unrelated.TapPackage");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open()))
                writer.Write("<Package Name=\"Unrelated\" Version=\"1.0.0\"><Files><File Path=\"marker.txt\"/></Files></Package>");
            using (var writer = new StreamWriter(zip.CreateEntry("marker.txt").Open())) writer.Write("complete unrelated payload");
        }
        new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true, OfflinePackagePath = archive });
        Assert.Contains(OpenTapHomeBootstrapper.ListInstalledPackages(home), package => package.Name == "Unrelated");
        Assert.False(StandaloneVisaReadiness.RequiresStandaloneReadiness(home));
        File.WriteAllText(Path.Combine(home.Root, "FixtureTui.dll"), "TUI prerequisite presence fixture");
        var vm = fixture.ViewModel; vm.OpenTapHomeOverride = home.Root; vm.Open(workspace.Root); vm.SelectProgram("sample");
        Assert.False(vm.HasUncompiledSources); Assert.Empty(vm.CompiledConflictProgramIds); Assert.True(vm.RequiresInstrumentLibrary);
        var window = fixture.Show(); fixture.Control<TabItem>("Workspace tab").IsSelected = true; AuthoringUiFixture.Drain();
        var readiness = fixture.Control<TextBlock>("Standalone VISA readiness");
        Assert.True(readiness.IsEffectivelyVisible);
        Assert.Contains("requires InstrumentComponents.OpenTap", readiness.Text);
        var before = Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        AuthoringUiFixture.Click(window.FindControl<Button>("CommandPaletteButton")!);
        var palette = Assert.Single(window.OwnedWindows);
        fixture.Control<TextBox>("Search commands", palette).Text = "external TUI"; AuthoringUiFixture.Drain();
        var run = Assert.Single(palette.GetVisualDescendants().OfType<Button>(), button => button.Content is string content && content == "Run command");
        Assert.False(run.IsEnabled);
        Assert.Contains(palette.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("requires InstrumentComponents.OpenTap", StringComparison.Ordinal) == true);
        Assert.False(await window.ExecuteCommandAsync("tui"));
        Assert.Contains("requires InstrumentComponents.OpenTap", vm.Error);
        var after = Directory.EnumerateFiles(home.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(home.Root, path), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        palette.Close();
        workspace.Manifest.Dependencies.RemoveAll(dependency => dependency.Package.Equals(AuthoringInstrumentCatalog.LibraryPackage, StringComparison.OrdinalIgnoreCase));
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        vm.Open(workspace.Root); AuthoringUiFixture.Drain();
        Assert.False(vm.RequiresInstrumentLibrary);
        Assert.Contains("optional", fixture.Control<TextBlock>("Standalone VISA readiness").Text);
    }
}
