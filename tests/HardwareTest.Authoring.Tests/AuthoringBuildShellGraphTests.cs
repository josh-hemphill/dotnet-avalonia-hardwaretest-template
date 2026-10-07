using System.Text.Json;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildShellGraphTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();
    [Fact]
    [Trait("Category", "AuthoringIntegration")]
    public void Real_external_Notes_graph_publishes_Avalonia_app_from_saved_inputs_without_original_obj_writes()
    {
        var repository = Repository();
        var notes = Path.Combine(repository, "src", "HardwareTest.ShellApps.Notes", "HardwareTest.ShellApps.Notes.csproj");
        var abstractions = Path.Combine(repository, "src", "HardwareTest.Shell.Abstractions");
        var before = ObjIdentities(Path.GetDirectoryName(notes)!, abstractions);
        var workspace = WithShell(notes);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions
        { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
        Assert.Contains(request.Inputs, i => i.Path == notes);
        Assert.Contains(request.Inputs, i => i.Path == Path.Combine(abstractions, "HardwareTest.Shell.Abstractions.csproj"));
        Assert.Contains(request.Inputs, i => i.Path == Path.Combine(repository, "Directory.Packages.props"));
        Assert.Contains(request.Inputs, i => i.Path.EndsWith("NotesView.axaml", StringComparison.Ordinal));
        Assert.Contains(request.Inputs, i => i.Path.Contains("avalonia", StringComparison.OrdinalIgnoreCase) && i.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(request.Inputs, i => i.Path.Contains("microsoft.netcore.app.runtime.win-x64", StringComparison.Ordinal));
        Assert.Contains(request.Inputs, i => i.Path.Contains("microsoft.netcore.app.host.osx-arm64", StringComparison.Ordinal));
        var output = AuthoringBuildSnapshotTests.Temp();
        var result = AuthoringBuildService.Execute(request, output);
        var assembly = Path.Combine(output, "shell-apps", "HardwareTest.ShellApps.Notes", "HardwareTest.ShellApps.Notes.dll");
        Assert.True(File.Exists(assembly));
        Assert.Contains(result.Receipt.Outputs, a => a.Path.Replace('\\', '/') == "shell-apps/HardwareTest.ShellApps.Notes/HardwareTest.ShellApps.Notes.dll");
        Assert.Equal(before, ObjIdentities(Path.GetDirectoryName(notes)!, abstractions));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("reference")]
    [InlineData("configuration")]
    [InlineData("lock")]
    [InlineData("package")]
    [Trait("Category", "AuthoringIntegration")]
    public void Copied_real_Notes_graph_rechecks_projects_configs_locks_and_resolved_payloads(string changed)
    {
        var repository = Repository();
        var fixture = AuthoringBuildSnapshotTests.Temp();
        foreach (var name in new[] { "Directory.Build.props", "Directory.Packages.props", "global.json" })
            File.Copy(Path.Combine(repository, name), Path.Combine(fixture, name));
        foreach (var name in new[] { "HardwareTest.ShellApps.Notes", "HardwareTest.Shell.Abstractions" })
            CopyDirectory(Path.Combine(repository, "src", name), Path.Combine(fixture, "src", name), excludeBuild: true);
        var notes = Path.Combine(fixture, "src", "HardwareTest.ShellApps.Notes", "HardwareTest.ShellApps.Notes.csproj");
        var originalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        var sourcePackages = originalPackages ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var packageRoot = changed == "package" ? AuthoringBuildSnapshotTests.Temp() : sourcePackages;
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in new[] { "HardwareTest.ShellApps.Notes", "HardwareTest.Shell.Abstractions" })
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "src", project, "packages.lock.json")));
            foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
                foreach (var package in framework.Value.EnumerateObject())
                    if (package.Value.TryGetProperty("resolved", out var version))
                        identities.Add(package.Name.ToLowerInvariant() + "/" + version.GetString()!.ToLowerInvariant());
        }
        if (changed == "package")
        {
            foreach (var id in identities) CopyDirectory(Path.Combine(sourcePackages, id), Path.Combine(packageRoot, id));
            // RuntimeIdentifiers also infer framework/host pack downloads absent from locks.
            CopyInferredFrameworkPackages(sourcePackages, packageRoot);
        }
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", packageRoot);
            var workspace = WithShell(notes);
            var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions
            { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
            var target = changed switch
            {
                "project" => notes,
                "reference" => Path.Combine(fixture, "src", "HardwareTest.Shell.Abstractions", "HardwareTest.Shell.Abstractions.csproj"),
                "configuration" => Path.Combine(fixture, "Directory.Packages.props"),
                "lock" => Path.Combine(fixture, "src", "HardwareTest.ShellApps.Notes", "packages.lock.json"),
                _ => request.Inputs.First(i => i.Path.StartsWith(packageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && i.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Path,
            };
            File.AppendAllText(target, " ");
            Assert.Contains("BUILD_INPUT_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Recheck(request)).Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", originalPackages);
            if (changed == "package") Directory.Delete(packageRoot, recursive: true);
        }
    }

    [Fact]
    public void Owned_package_cache_includes_WindowsDesktop_and_other_inferred_framework_payloads()
    {
        var source = AuthoringBuildSnapshotTests.Temp(); var target = AuthoringBuildSnapshotTests.Temp();
        var families = new[] { "microsoft.netcore.app.runtime.win-x64", "microsoft.aspnetcore.app.runtime.win-x64",
            "microsoft.windowsdesktop.app.runtime.win-x64", "Microsoft.WindowsDesktop.App.Ref" };
        foreach (var family in families.Append("unrelated.package"))
        {
            var directory = Path.Combine(source, family, "10.0.0"); Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "payload.dll"), family);
        }
        CopyInferredFrameworkPackages(source, target);
        foreach (var family in families)
            Assert.Equal(family, File.ReadAllText(Path.Combine(target, family, "10.0.0", "payload.dll")));
        Assert.False(Directory.Exists(Path.Combine(target, "unrelated.package")));
    }

    private static void CopyInferredFrameworkPackages(string source, string target)
    {
        foreach (var directory in Directory.GetDirectories(source))
            if (new[] { "microsoft.netcore.app.", "microsoft.aspnetcore.app.", "microsoft.windowsdesktop.app." }
                .Any(family => Path.GetFileName(directory).StartsWith(family, StringComparison.OrdinalIgnoreCase)))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    [Theory]
    [InlineData("$([System.IO.File]::ReadAllText('undeclared.txt'))")]
    [InlineData("$([System.Environment]::GetEnvironmentVariable('HOME'))")]
    [InlineData("$([MSBuild]::GetPathOfFileAbove('undeclared.props'))")]
    public void Ancestor_configuration_with_undeclared_evaluation_inputs_is_rejected_before_SDK_evaluation(string function)
    {
        var fixture = AuthoringBuildSnapshotTests.Temp();
        var projectDirectory = Path.Combine(fixture, "src");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "Shell.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture, "Directory.Build.props"),
            "<Project><PropertyGroup><CustomValue>" + function + "</CustomValue></PropertyGroup></Project>");
        var workspace = WithShell(project);
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.CaptureSaved(workspace,
            new PackOptions { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true }));
        Assert.Contains("BUILD_SHELL_INPUTS", error.Message);
        Assert.Contains("custom evaluation property functions", error.Message);
        Assert.False(Directory.Exists(Path.Combine(projectDirectory, "obj")));
    }

    private static AuthoringWorkspace WithShell(string project)
    {
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        workspace.Manifest.ShellAppProjects.Add(project);
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        return workspace;
    }

    private static string Repository()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Directory.Build.props"))) directory = Path.GetDirectoryName(directory)!;
        return directory;
    }

    private static string[] ObjIdentities(params string[] projects) => projects
        .SelectMany(p => Directory.Exists(Path.Combine(p, "obj"))
            ? Directory.GetFiles(Path.Combine(p, "obj"), "*", SearchOption.AllDirectories) : [])
        .Order(StringComparer.Ordinal).Select(p => p + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();

    private static void CopyDirectory(string source, string target, bool excludeBuild = false)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            if (!excludeBuild || Path.GetFileName(directory) is not ("bin" or "obj" or ".git"))
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), excludeBuild);
    }
}
