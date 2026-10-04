using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildEnvironmentGuardTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData("OutputPath")]
    [InlineData("outputpath")]
    [InlineData("BaseOutputPath")]
    [InlineData("OutDir")]
    [InlineData("IntermediateOutputPath")]
    [InlineData("BaseIntermediateOutputPath")]
    [InlineData("MSBuildProjectExtensionsPath")]
    [InlineData("PublishDir")]
    [InlineData("CustomBeforeMicrosoftCommonTargets")]
    [InlineData("DirectoryBuildPropsPath")]
    [InlineData("DirectoryBuildTargetsPath")]
    [InlineData("DirectoryPackagesPropsPath")]
    [InlineData("RestorePackagesPath")]
    [InlineData("NuGetPackageRoot")]
    [InlineData("RestoreConfigFile")]
    [InlineData("RestoreSources")]
    [InlineData("RestoreFallbackFolders")]
    [InlineData("MSBuildUserExtensionsPath")]
    [InlineData("MSBuildStartupDirectory")]
    [InlineData("NUGET_HTTP_CACHE_PATH")]
    [InlineData("NUGET_SCRATCH")]
    [InlineData("MSBUILDTEMPPATH")]
    [InlineData("CscToolPath")]
    [InlineData("DocumentationFile")]
    [InlineData("AssemblyOriginatorKeyFile")]
    public void Inherited_redirect_properties_are_rejected_before_SDK_evaluation(string name)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, Path.GetFullPath("redirected"));
            var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureEnvironment());
            Assert.Contains("BUILD_ENVIRONMENT", error.Message);
            Assert.Contains(name, error.Message);
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    [Theory]
    [InlineData("MSBuildToolsPath")]
    [InlineData("MSBuildSDKsPath")]
    [InlineData("RoslynTargetsPath")]
    [InlineData("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR")]
    [InlineData("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR")]
    public void SDK_environment_bindings_must_resolve_inside_declared_SDK(string name)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, AuthoringBuildSnapshotTests.Temp());
            Assert.Contains("BUILD_CONTAINMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
                AuthoringBuildService.CaptureEnvironment()).Message);
            Environment.SetEnvironmentVariable(name, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
            Assert.Contains(name, AuthoringBuildService.CaptureEnvironment().Keys);
        }
        finally { Environment.SetEnvironmentVariable(name, previous); }
    }

    [Fact]
    public void SDK_output_redirect_cannot_touch_previous_outputs_during_capture_or_prepare()
    {
        var projectRoot = AuthoringBuildSnapshotTests.Temp();
        var project = Path.Combine(projectRoot, "Shell.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), "System.Console.WriteLine(\"saved\");");
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        workspace.Manifest.ShellAppProjects.Add(project);
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        var home = AuthoringBuildSnapshotTests.Home(workspace);
        var output = AuthoringBuildSnapshotTests.Temp();
        Directory.CreateDirectory(Path.Combine(output, "net10.0"));
        var previousDll = Path.Combine(output, "net10.0", "Shell.dll");
        File.WriteAllText(previousDll, "previous final output");
        var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(p => (p, File.ReadAllBytes(p))).ToArray();
        var previous = Environment.GetEnvironmentVariable("OutputPath");
        try
        {
            Environment.SetEnvironmentVariable("OutputPath", null);
            var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true });
            Environment.SetEnvironmentVariable("OutputPath", output + Path.DirectorySeparatorChar);
            Assert.Contains("BUILD_ENVIRONMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
                AuthoringBuildService.CaptureSaved(workspace, new PackOptions { Home = home, Offline = true })).Message);
            Assert.Contains("BUILD_ENVIRONMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
                AuthoringBuildService.Prepare(request)).Message);
            Assert.Equal(before.Select(f => f.p), Directory.GetFiles(output, "*", SearchOption.AllDirectories));
            foreach (var file in before) Assert.Equal(file.Item2, File.ReadAllBytes(file.p));
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "obj")));
            Assert.False(Directory.Exists(Path.Combine(projectRoot, "bin")));
        }
        finally { Environment.SetEnvironmentVariable("OutputPath", previous); }
    }
}
