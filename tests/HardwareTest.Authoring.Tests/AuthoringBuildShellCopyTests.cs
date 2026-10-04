using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildShellCopyTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    public static IEnumerable<object[]> UnsafeMetadata()
    {
        foreach (var name in new[] { "Link", "LinkBase", "TargetPath", "RelativePath", "DestinationSubDirectory" })
            foreach (var casing in new[] { 0, 1, 2 })
                foreach (var attribute in new[] { false, true })
                    foreach (var form in new[] { "literal", "property", "metadata", "escape" })
                        yield return [CaseName(name, casing), attribute, form, casing];
    }

    [Theory]
    [MemberData(nameof(UnsafeMetadata))]
    public void Copy_metadata_cannot_overwrite_external_sentinel(string name, bool attribute, string form, int casing)
    {
        var (workspace, project) = Workspace();
        var sentinel = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "sentinel.txt"); File.WriteAllText(sentinel, "previous");
        var escaped = string.Concat(Enumerable.Repeat("../", 30))
            + Path.GetRelativePath(Path.GetPathRoot(sentinel)!, sentinel).Replace('\\', '/');
        var value = form switch
        {
            "property" => "$(COPY_DEST)",
            "metadata" => "%(None.CopyDestination)",
            "escape" => escaped.Replace(".", "%2e", StringComparison.Ordinal).Replace("/", "%2f", StringComparison.Ordinal),
            _ => escaped
        };
        var item = new XElement(CaseName("None", casing), new XAttribute("Update", "payload.txt"), new XAttribute("CopyToPublishDirectory", "Always"));
        if (attribute) item.Add(new XAttribute(name, value)); else item.Add(new XElement(name, value));
        item.Add(new XElement("CopyDestination", escaped));
        var xml = XDocument.Load(project);
        xml.Root!.Element("PropertyGroup")!.Add(new XElement("COPY_DEST", escaped));
        xml.Root.Add(new XElement("ItemGroup", item)); xml.Save(project);
        var output = AuthoringBuildSnapshotTests.Temp(); File.WriteAllText(Path.Combine(output, "previous"), "keep");
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(
            AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true }), output));
        Assert.Contains("copy-output metadata", error.Message);
        Assert.Equal("previous", File.ReadAllText(sentinel));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "previous")));
        Assert.Single(Directory.GetFiles(output));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

    public static IEnumerable<object[]> ReferenceOverrides()
    {
        foreach (var name in new[] { "AdditionalProperties", "GlobalPropertiesToRemove", "SetConfiguration", "SetPlatform", "SetTargetFramework", "Targets", "Properties", "UndefineProperties" })
            foreach (var casing in new[] { 0, 1, 2 })
                foreach (var attribute in new[] { false, true })
                    yield return [CaseName(name, casing), attribute, casing];
    }

    [Theory]
    [MemberData(nameof(ReferenceOverrides))]
    public void Project_reference_cannot_remove_or_override_checked_global_properties(string name, bool attribute, int casing)
    {
        var (workspace, project) = Workspace();
        var reference = new XElement(CaseName("ProjectReference", casing), new XAttribute("Include", "child/Child.csproj"));
        if (attribute) reference.Add(new XAttribute(name, "SourceRevisionId;PublishDir"));
        else reference.Add(new XElement(name, "SourceRevisionId;PublishDir"));
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", reference)); xml.Save(project);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true }));
        Assert.Contains("project-reference write-profile overrides", error.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Case_insensitive_reference_hint_metadata_is_captured(bool attribute, int casing)
    {
        var (workspace, project) = Workspace();
        var assembly = Path.Combine(Path.GetDirectoryName(project)!, "lib", "SavedReference.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.Copy(typeof(AuthoringBuildService).Assembly.Location, assembly);
        var reference = new XElement(CaseName("Reference", casing), new XAttribute("Include", "SavedReference"));
        if (attribute) reference.Add(new XAttribute(CaseName("HintPath", casing), "lib/SavedReference.dll"));
        else reference.Add(new XElement(CaseName("HintPath", casing), "lib/SavedReference.dll"));
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", reference)); xml.Save(project);
        var request = AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        Assert.Contains(request.Inputs, input => input.Path == assembly
            && input.Sha256 == AuthoringBuildService.Hash(File.ReadAllBytes(assembly)));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void Literal_contained_copy_metadata_publishes_saved_payload(bool attribute, int casing)
    {
        var (workspace, project) = Workspace();
        var item = new XElement(CaseName("None", casing), new XAttribute("Update", "payload.txt"), new XAttribute("CopyToPublishDirectory", "Always"));
        if (attribute) item.Add(new XAttribute(CaseName("Link", casing), "assets/payload.txt"));
        else item.Add(new XElement(CaseName("Link", casing), "assets/payload.txt"));
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", item)); xml.Save(project);
        var request = AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var output = AuthoringBuildSnapshotTests.Temp();
        AuthoringBuildService.Execute(request, output);
        Assert.Equal("saved payload", File.ReadAllText(Path.Combine(output, "shell-apps", "Shell", "assets", "payload.txt")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

    private static string CaseName(string name, int casing) => casing switch
    {
        1 => name.ToLowerInvariant(),
        2 => string.Concat(name.Select((character, index) => index % 2 == 0 ? char.ToLowerInvariant(character) : char.ToUpperInvariant(character))),
        _ => name
    };

    private static (AuthoringWorkspace Workspace, string Project) Workspace()
    {
        var root = AuthoringBuildSnapshotTests.Temp(); var project = Path.Combine(root, "Shell.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"saved\");");
        File.WriteAllText(Path.Combine(root, "payload.txt"), "saved payload");
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        workspace.Manifest.ShellAppProjects.Add(project); AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        return (workspace, project);
    }
}
