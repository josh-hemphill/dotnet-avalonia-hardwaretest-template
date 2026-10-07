using System.IO.Compression;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class LibraryDependencyRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-library-recovery-" + Guid.NewGuid().ToString("N"));
    public LibraryDependencyRecoveryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Declaration_is_staged_undoable_and_saved_with_workspace_sources()
    {
        new AuthoringWorkspaceInitializer().Create(new(_root, "Library workspace", "Library workspace"));
        var manifestPath = Path.Combine(_root, "authoring.json");
        var original = File.ReadAllBytes(manifestPath);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root);
        Assert.True(vm.CanDeclareLibraryDependency);
        vm.DeclareLibraryDependency();
        Assert.True(vm.HasUnsavedChanges); Assert.True(vm.CanUndoWorkspace);
        Assert.Equal(original, File.ReadAllBytes(manifestPath));
        Assert.False(vm.CanDeclareLibraryDependency);
        vm.UndoWorkspace();
        Assert.True(vm.CanDeclareLibraryDependency); Assert.False(vm.HasUnsavedChanges);
        vm.RedoWorkspace();
        Assert.False(vm.CanDeclareLibraryDependency);
        Assert.True(vm.SaveAll().Succeeded);
        Assert.False(vm.HasUnsavedChanges);
        vm.Open(_root);
        Assert.Contains(vm.Workspace!.Manifest.Dependencies, d => d.Package == AuthoringInstrumentCatalog.LibraryPackage && d.Version == "^0.1.0");
        Assert.Contains(new AuthoringDocumentStore(_root).LoadWorkspace().Document!.Manifest.Dependencies, d => d.Package == AuthoringInstrumentCatalog.LibraryPackage);
    }

    [Fact]
    public void Failed_import_keeps_declared_dependency_and_saved_workspace_bytes()
    {
        new AuthoringWorkspaceInitializer().Create(new(_root, "Library workspace", "Library workspace"));
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.DeclareLibraryDependency();
        Assert.True(vm.SaveAll().Succeeded);
        var manifest = File.ReadAllBytes(Path.Combine(_root, "authoring.json"));
        var package = Path.Combine(_root, "wrong.zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("package.xml").Open())) writer.Write("<Package Name=\"InstrumentComponents.OpenTap\" Version=\"2.0.0\"><Files/></Package>");
        Assert.Throws<AuthoringWorkspaceException>(() => new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace!, new()
        { HomeDirectory = Path.Combine(_root, "home"), Offline = true, OfflinePackagePath = package }));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(_root, "authoring.json")));
        Assert.False(vm.HasUnsavedChanges);
        Assert.False(vm.CanDeclareLibraryDependency);
    }

    [Fact]
    public void Failed_save_retains_the_staged_declaration_and_dirty_state()
    {
        new AuthoringWorkspaceInitializer().Create(new(_root, "Library workspace", "Library workspace"));
        var path = Path.Combine(_root, "authoring.json"); var original = File.ReadAllBytes(path);
        var vm = new AuthoringWorkspaceViewModel(); vm.Open(_root); vm.DeclareLibraryDependency();
        vm.WorkspaceManifestReplacement = (_, _) => throw new IOException("Simulated write failure");
        Assert.False(vm.SaveAll().Succeeded);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(vm.CanDeclareLibraryDependency);
        Assert.Contains(vm.Workspace!.Manifest.Dependencies, d => d.Package == AuthoringInstrumentCatalog.LibraryPackage);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
