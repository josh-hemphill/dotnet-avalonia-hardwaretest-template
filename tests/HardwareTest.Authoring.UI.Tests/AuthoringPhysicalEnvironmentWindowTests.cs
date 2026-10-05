using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringPhysicalEnvironmentWindowTests
{
    [AvaloniaTheory]
    [InlineData(true, "runtime")]
    [InlineData(false, "runtime")]
    [InlineData(true, "home")]
    [InlineData(false, "home")]
    [InlineData(true, "metadata")]
    [InlineData(false, "metadata")]
    [InlineData(true, "payload")]
    [InlineData(false, "payload")]
    public void Rendered_pack_rejects_unsafe_existing_optional_or_undeclared_engine_home(bool optional, string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new AuthoringUiFixture();
        var workspace = AuthoringWorkspaceLoader.Load(fixture.WorkspaceRoot);
        workspace.Manifest.Package.Name = "Physical environment control";
        var home = new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = Path.Combine(workspace.Root, "selected-home"), Offline = true });
        workspace.Manifest.Dependencies.Clear(); workspace.Manifest.OptionalDependencies.Clear();
        if (optional) workspace.Manifest.OptionalDependencies.Add(new() { Package = "OpenTAP", Version = "^9.32.2" });
        workspace.Manifest.OptionalDependencies.Add(new() { Package = "Missing optional control", Version = "^1.0.0" });
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        fixture.ViewModel.Open(workspace.Root); fixture.ViewModel.OpenTapHomeOverride = home.Root;
        var window = fixture.Show(); window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 4; AuthoringUiFixture.Drain();
        Assert.True(fixture.ViewModel.CanPack); Assert.True(fixture.Control<Button>("Pack workspace").IsEnabled);
        var source = Path.Combine(workspace.Root, "sample.TapPlan"); var sourceBytes = File.ReadAllBytes(source);
        var outside = Path.Combine(workspace.Root, "outside-home"); Directory.CreateDirectory(outside);
        var selected = home.Root;
        if (kind == "home")
        {
            selected = Path.Combine(workspace.Root, "linked-home"); Directory.CreateSymbolicLink(selected, home.Root);
        }
        else if (kind is "metadata" or "runtime")
        {
            var file = kind == "metadata" ? Path.Combine(home.Root, "Packages", "OpenTAP", "package.xml") : Path.Combine(home.Root, "OpenTap.dll");
            var target = Path.Combine(outside, Path.GetFileName(file)); File.Move(file, target); File.CreateSymbolicLink(file, target);
        }
        else
        {
            var package = Path.Combine(home.Root, "Packages", "OpenTAP"); var metadata = Path.Combine(package, "package.xml");
            var document = XDocument.Load(metadata); var ns = document.Root!.Name.Namespace;
            document.Root.Add(new XElement(ns + "Files", new XElement(ns + "File", new XAttribute("Path", "escaped.dll")))); document.Save(metadata);
            var target = Path.Combine(outside, "escaped.dll"); File.Copy(Path.Combine(home.Root, "OpenTap.dll"), target); File.CreateSymbolicLink(Path.Combine(package, "escaped.dll"), target);
        }
        fixture.ViewModel.OpenTapHomeOverride = selected + Path.DirectorySeparatorChar; AuthoringUiFixture.Drain();
        Assert.False(fixture.ViewModel.CanPack); Assert.False(fixture.Control<Button>("Pack workspace").IsEnabled);
        Assert.Contains("unsafe", fixture.Control<TextBlock>("Pack save guard").Text!, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.ViewModel.HasUnsavedChanges); Assert.Equal(sourceBytes, File.ReadAllBytes(source));
        Assert.Null(fixture.ViewModel.LastBuildReceipt); Assert.Equal("Not checked", fixture.ViewModel.CompatibilityState);
    }
}
