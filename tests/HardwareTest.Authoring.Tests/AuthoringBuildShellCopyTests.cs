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
            foreach (var attribute in new[] { false, true })
                foreach (var form in new[] { "literal", "property", "metadata", "escape" })
                    yield return [name, attribute, form];
    }

    [Theory]
    [MemberData(nameof(UnsafeMetadata))]
    public void Copy_metadata_cannot_overwrite_external_sentinel(string name, bool attribute, string form)
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
        var item = new XElement("None", new XAttribute("Update", "payload.txt"), new XAttribute("CopyToPublishDirectory", "Always"));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_reference_cannot_undefine_checked_global_properties(bool attribute)
    {
        var (workspace, project) = Workspace();
        var reference = new XElement("ProjectReference", new XAttribute("Include", "child/Child.csproj"));
        if (attribute) reference.Add(new XAttribute("UndefineProperties", "SourceRevisionId;PublishDir"));
        else reference.Add(new XElement("UndefineProperties", "SourceRevisionId;PublishDir"));
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", reference)); xml.Save(project);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true }));
        Assert.Contains("project-reference write-profile overrides", error.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Literal_contained_copy_metadata_publishes_saved_payload(bool attribute)
    {
        var (workspace, project) = Workspace();
        var item = new XElement("None", new XAttribute("Update", "payload.txt"), new XAttribute("CopyToPublishDirectory", "Always"));
        if (attribute) item.Add(new XAttribute("Link", "assets/payload.txt"));
        else item.Add(new XElement("Link", "assets/payload.txt"));
        var xml = XDocument.Load(project); xml.Root!.Add(new XElement("ItemGroup", item)); xml.Save(project);
        var request = AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        var output = AuthoringBuildSnapshotTests.Temp();
        AuthoringBuildService.Execute(request, output);
        Assert.Equal("saved payload", File.ReadAllText(Path.Combine(output, "shell-apps", "Shell", "assets", "payload.txt")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
    }

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
