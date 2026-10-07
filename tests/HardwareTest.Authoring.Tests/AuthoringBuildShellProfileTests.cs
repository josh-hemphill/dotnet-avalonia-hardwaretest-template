using System.Xml.Linq;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildShellProfileTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reference_copy_folder_overrides_cannot_write_external_sentinel(bool inherited)
    {
        var (workspace, project) = Workspace();
        var externalDirectory = AuthoringBuildSnapshotTests.Temp();
        var external = Path.Combine(externalDirectory, "System.Runtime.dll"); File.WriteAllText(external, "old");
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        var previous = Environment.GetEnvironmentVariable("RefAssembliesFolderName");
        try
        {
            if (inherited) Environment.SetEnvironmentVariable("RefAssembliesFolderName", EscapingRelativePath(externalDirectory));
            else
            {
                var xml = XDocument.Load(project);
                xml.Root!.Element("PropertyGroup")!.Add(new XElement("RefAssembliesFolderName", EscapingRelativePath(externalDirectory)));
                xml.Save(project);
            }
            var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace,
                new PackOptions { Home = home, Offline = true }));
            Assert.Contains("RefAssembliesFolderName", error.Message);
            Assert.Equal("old", File.ReadAllText(external));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
        }
        finally { Environment.SetEnvironmentVariable("RefAssembliesFolderName", previous); }
    }

    [Theory]
    [InlineData("'$(SourceRevisionId)' == 'local'")]
    [InlineData("'$(SourceRevisionDate)' == '1970-01-01T00:00:00Z'")]
    [InlineData("'$(RestorePackagesWithLockFile)' == 'true'")]
    [InlineData("$([System.String]::Copy('$(MSBuildProjectDirectory)').Contains('authoring-shell-restore-'))")]
    [Trait("Category", "AuthoringIntegration")]
    public void Conditional_identities_are_checked_on_copied_paths_with_actual_restore_properties(string condition)
        => AssertConditionalBlocked(condition, duringCapture: true);

    [Theory]
    [InlineData("'$(_IsPublishing)' == 'true'")]
    [InlineData("'$(_CommandLineDefinedOutputPath)' == 'true'")]
    [InlineData("'$(PublishDir)' != ''")]
    [InlineData("$([System.String]::Copy('$(MSBuildProjectDirectory)').Contains('authoring-build-'))")]
    [Trait("Category", "AuthoringIntegration")]
    public void Conditional_identities_are_checked_with_actual_staged_publish_properties(string condition)
        => AssertConditionalBlocked(condition, duringCapture: false);

    private static void AssertConditionalBlocked(string condition, bool duringCapture)
    {
        var (workspace, project) = Workspace();
        var external = Path.Combine(AuthoringBuildSnapshotTests.Temp(), "sentinel.dll"); File.WriteAllText(external, "old");
        var previous = Environment.GetEnvironmentVariable("SDK_ESCAPE_IDENTITY");
        try
        {
            Environment.SetEnvironmentVariable("SDK_ESCAPE_IDENTITY", EscapingRelativePath(Path.ChangeExtension(external, null)));
            var xml = XDocument.Load(project);
            xml.Root!.Add(new XElement("PropertyGroup", new XAttribute("Condition", condition),
                new XElement("AssemblyName", "$([System.String]::Copy('$(SDK_ESCAPE_IDENTITY)'))")));
            xml.Save(project);
            var options = new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true };
            var output = AuthoringBuildSnapshotTests.Temp(); File.WriteAllText(Path.Combine(output, "previous"), "keep");
            AuthoringWorkspaceException error;
            if (duringCapture) error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace, options));
            else
            {
                var request = AuthoringBuildService.CaptureSaved(workspace, options);
                error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Execute(request, output));
            }
            Assert.Contains("AssemblyName", error.Message);
            Assert.Contains("write profile", error.Message);
            Assert.Equal("old", File.ReadAllText(external));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(output, "previous")));
            Assert.Single(Directory.GetFiles(output));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
        }
        finally { Environment.SetEnvironmentVariable("SDK_ESCAPE_IDENTITY", previous); }
    }

    [Fact]
    public void Project_reference_cannot_change_checked_write_properties()
    {
        var (workspace, project) = Workspace();
        var xml = XDocument.Load(project);
        xml.Root!.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "child/Child.csproj"),
            new XElement("AdditionalProperties", "SourceRevisionId=unchecked"))));
        xml.Save(project);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true }));
        Assert.Contains("project-reference write-profile overrides", error.Message);
    }

    [Fact]
    [Trait("Category", "AuthoringIntegration")]
    public void Write_profile_evaluation_uses_frozen_environment_and_SDK_host()
    {
        var (_, project) = Workspace();
        var xml = XDocument.Load(project);
        xml.Root!.Element("PropertyGroup")!.Add(new XElement("AssemblyName", "$(SDK_PROFILE_NAME)")); xml.Save(project);
        var previousName = Environment.GetEnvironmentVariable("SDK_PROFILE_NAME");
        var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SDK_PROFILE_NAME", "FrozenShell");
            var environment = AuthoringBuildService.CaptureEnvironment();
            var profile = AuthoringBuildService.ShellWriteProfile(environment["NUGET_PACKAGES"]!);
            Environment.SetEnvironmentVariable("SDK_PROFILE_NAME", "../../escaped");
            Environment.SetEnvironmentVariable("DOTNET_ROOT", AuthoringBuildSnapshotTests.Temp());
            AuthoringBuildService.ValidateShellWriteProfile(project, Path.GetDirectoryName(project)!, profile, environment);
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(project)!, "obj")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SDK_PROFILE_NAME", previousName);
            Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
        }
    }

    private static (AuthoringWorkspace Workspace, string Project) Workspace()
    {
        var root = AuthoringBuildSnapshotTests.Temp(); var project = Path.Combine(root, "Shell.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><PreserveCompilationReferences>true</PreserveCompilationReferences></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"saved\");");
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        workspace.Manifest.ShellAppProjects.Add(project); AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        return (workspace, project);
    }

    private static string EscapingRelativePath(string absolute) => string.Concat(Enumerable.Repeat("../", 30))
        + Path.GetRelativePath(Path.GetPathRoot(absolute)!, absolute).Replace('\\', '/');
}
