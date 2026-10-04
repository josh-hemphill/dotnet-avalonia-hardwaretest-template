using System.Text.Json;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildBoundaryTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_MSBuild_environment_property_changed_after_capture_cannot_publish(bool initiallyAbsent)
    {
        var previous = Environment.GetEnvironmentVariable("SNAPSHOT_FLAG");
        try
        {
            Environment.SetEnvironmentVariable("SNAPSHOT_FLAG", initiallyAbsent ? null : "CAPTURED_FLAG");
            var workspace = ShellWorkspace();
            var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions
            { Home = AuthoringBuildSnapshotTests.Home(workspace), Offline = true });
            if (initiallyAbsent) Assert.DoesNotContain(request.Environment, e => e.Name == "SNAPSHOT_FLAG");
            else Assert.Contains(request.Environment, e => e.Name == "SNAPSHOT_FLAG" && e.ValueSha256 is not null);
            Environment.SetEnvironmentVariable("SNAPSHOT_FLAG", "CHANGED_FLAG");
            var output = AuthoringBuildSnapshotTests.Temp();
            File.WriteAllText(Path.Combine(output, "previous"), "old");
            Assert.Contains("BUILD_ENVIRONMENT_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() =>
                AuthoringBuildService.Execute(request, output)).Message);
            Assert.Equal("old", File.ReadAllText(Path.Combine(output, "previous")));
            Assert.Single(Directory.GetFiles(output));
        }
        finally { Environment.SetEnvironmentVariable("SNAPSHOT_FLAG", previous); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("package")]
    [InlineData("home")]
    [InlineData("intermediate")]
    public void Compatibility_provider_cannot_change_captured_staged_inputs(string input)
    {
        var workspace = ShellWorkspace();
        var checker = new StagedInputMutationChecker(input);
        var request = AuthoringBuildService.CaptureSaved(workspace, new PackOptions
        { Home = AuthoringBuildSnapshotTests.Home(workspace), Compat = checker, Offline = true });
        var output = AuthoringBuildSnapshotTests.Temp();
        File.WriteAllText(Path.Combine(output, "previous"), "old");
        Assert.Contains("BUILD_STAGED_CHANGED", Assert.Throws<AuthoringWorkspaceException>(() =>
            AuthoringBuildService.Execute(request, output)).Message);
        Assert.True(checker.Changed);
        Assert.Equal("old", File.ReadAllText(Path.Combine(output, "previous")));
        Assert.Single(Directory.GetFiles(output));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Versioned_receipt_and_build_result_roundtrip_with_owned_collections(int version)
    {
        var map = new[] { new AuthoringCompileMapEntry(Guid.NewGuid(), Guid.NewGuid(), "authoring") };
        var compile = new AuthoringCompileResult("sample", "planhash", map);
        var included = new[] { "sample" };
        var receipt = new AuthoringBuildReceipt(version, "id", DateTimeOffset.UnixEpoch, included, ["excluded"],
            [new("sample", 4, "sourcehash")], [new("source", "hash", "target")], [new("Basic", "1.0")],
            [new("check", "injected evidence", false)], [compile], [new("artifact", "hash")], 7, [new("ENV", "hash")]);
        included[0] = "changed"; map[0] = new(Guid.Empty, null, "changed");
        var original = new AuthoringBuildResult(new("package", "version", ["artifact"], [new("Basic", "1.0")]), receipt);
        var json = JsonSerializer.Serialize(original, AuthoringBuildJsonContext.Default.AuthoringBuildResult);
        var result = JsonSerializer.Deserialize(json, AuthoringBuildJsonContext.Default.AuthoringBuildResult)!;
        Assert.Equal(json, JsonSerializer.Serialize(result, AuthoringBuildJsonContext.Default.AuthoringBuildResult));
        Assert.Equal(version, result.Receipt.SchemaVersion);
        Assert.Equal("sample", result.Receipt.IncludedPlans.Single());
        Assert.Equal("authoring", result.Receipt.Compilation.Single().SourceMap.Single().Kind);
        Assert.Equal(7, result.Receipt.WorkspaceSavedRevision);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Receipt.IncludedPlans)[0] = "mutable");
        Assert.Throws<NotSupportedException>(() => ((IList<AuthoringCompileMapEntry>)result.Receipt.Compilation.Single().SourceMap).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)result.Manifest.Files).Clear());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publication_retires_previous_owned_outputs_transactionally_and_preserves_unrelated_files(bool fail)
    {
        var output = AuthoringBuildSnapshotTests.Temp();
        Directory.CreateDirectory(Path.Combine(output, "shell-apps", "removed"));
        File.WriteAllText(Path.Combine(output, "old.TapPackage"), "old package");
        File.WriteAllText(Path.Combine(output, "shell-apps", "removed", "app.dll"), "old shell");
        File.WriteAllText(Path.Combine(output, "unrelated.txt"), "retain");
        WriteReceipt(output, "old.TapPackage", Path.Combine("shell-apps", "removed", "app.dll"));
        var oldReceipt = File.ReadAllBytes(Path.Combine(output, AuthoringBuildService.ReceiptFileName));
        var staging = AuthoringBuildSnapshotTests.Temp();
        File.WriteAllText(Path.Combine(staging, "new.TapPackage"), "new package");
        WriteReceipt(staging, "new.TapPackage");
        void Move(string from, string to)
        {
            Assert.False(File.Exists(Path.Combine(output, "old.TapPackage")));
            if (fail) throw new IOException("Injected move failure after retirement");
            File.Move(from, to, true);
        }
        if (fail)
        {
            Assert.Throws<IOException>(() => AuthoringBuildService.Publish(staging, output, default, Move));
            Assert.Equal("old package", File.ReadAllText(Path.Combine(output, "old.TapPackage")));
            Assert.Equal("old shell", File.ReadAllText(Path.Combine(output, "shell-apps", "removed", "app.dll")));
            Assert.Equal(oldReceipt, File.ReadAllBytes(Path.Combine(output, AuthoringBuildService.ReceiptFileName)));
            Assert.False(File.Exists(Path.Combine(output, "new.TapPackage")));
        }
        else
        {
            AuthoringBuildService.Publish(staging, output, default, Move);
            Assert.False(File.Exists(Path.Combine(output, "old.TapPackage")));
            Assert.False(File.Exists(Path.Combine(output, "shell-apps", "removed", "app.dll")));
            Assert.Equal("new package", File.ReadAllText(Path.Combine(output, "new.TapPackage")));
        }
        Assert.Equal("retain", File.ReadAllText(Path.Combine(output, "unrelated.txt")));
    }

    [Fact]
    public void Modified_previously_owned_output_blocks_retirement()
    {
        var output = AuthoringBuildSnapshotTests.Temp();
        File.WriteAllText(Path.Combine(output, "old"), "old"); WriteReceipt(output, "old");
        File.WriteAllText(Path.Combine(output, "old"), "user changed");
        var staging = AuthoringBuildSnapshotTests.Temp(); File.WriteAllText(Path.Combine(staging, "new"), "new");
        Assert.Contains("BUILD_OUTPUT_CONFLICT", Assert.Throws<AuthoringWorkspaceException>(() =>
            AuthoringBuildService.Publish(staging, output, default)).Message);
        Assert.Equal("user changed", File.ReadAllText(Path.Combine(output, "old")));
        Assert.False(File.Exists(Path.Combine(output, "new")));
    }

    private static void WriteReceipt(string directory, params string[] paths)
    {
        var receipt = new AuthoringBuildReceipt(1, "id", DateTimeOffset.UnixEpoch, [], [], [], [], [], [], [],
            paths.Select(p => new AuthoringBuildOutput(p, AuthoringBuildService.Hash(File.ReadAllBytes(Path.Combine(directory, p))))).ToArray());
        File.WriteAllText(Path.Combine(directory, AuthoringBuildService.ReceiptFileName),
            JsonSerializer.Serialize(receipt, AuthoringBuildJsonContext.Default.AuthoringBuildReceipt));
    }

    [Fact]
    public void Previous_receipt_cannot_retire_an_artifact_outside_output()
    {
        var external = AuthoringBuildSnapshotTests.Temp();
        var externalFile = Path.Combine(external, "owned"); File.WriteAllText(externalFile, "retain");
        var output = AuthoringBuildSnapshotTests.Temp();
        WriteReceipt(output, Path.Combine("..", Path.GetFileName(external), "owned"));
        var staging = AuthoringBuildSnapshotTests.Temp(); File.WriteAllText(Path.Combine(staging, "new"), "new");
        Assert.Contains("BUILD_CONTAINMENT", Assert.Throws<AuthoringWorkspaceException>(() =>
            AuthoringBuildService.Publish(staging, output, default)).Message);
        Assert.Equal("retain", File.ReadAllText(externalFile));
        Assert.False(File.Exists(Path.Combine(output, "new")));
    }

    private static AuthoringWorkspace ShellWorkspace()
    {
        var projectRoot = AuthoringBuildSnapshotTests.Temp();
        var project = Path.Combine(projectRoot, "Shell.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>$(SNAPSHOT_FLAG)</DefineConstants></PropertyGroup><ItemGroup><PackageReference Include="Avalonia" Version="12.1.2" /></ItemGroup></Project>
            """);
        File.WriteAllText(Path.Combine(projectRoot, "Program.cs"), "System.Console.WriteLine(\"captured\");");
        File.WriteAllText(Path.Combine(projectRoot, "Directory.Build.props"), "<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>");
        var workspace = AuthoringWorkspaceLoader.Load(AuthoringBuildSnapshotTests.Workspace());
        workspace.Manifest.ShellAppProjects.Add(project);
        AuthoringWorkspaceLoader.SaveManifest(workspace.Root, workspace.Manifest);
        return workspace;
    }

    private sealed class StagedInputMutationChecker(string input) : ITuiCompatChecker
    {
        public bool Changed { get; private set; }
        public TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome)
        {
            var staging = Path.GetDirectoryName(workspace.Root)!;
            var project = workspace.Manifest.ShellAppProjects.Single();
            var path = input switch
            {
                "source" => Path.Combine(Path.GetDirectoryName(project)!, "Program.cs"),
                "configuration" => Path.Combine(Path.GetDirectoryName(project)!, "Directory.Build.props"),
                "package" => Directory.GetFiles(Path.Combine(staging, "shell-packages"), "*.dll", SearchOption.AllDirectories).First(),
                "intermediate" => Path.Combine(Path.GetDirectoryName(project)!, "obj", "injected.props"),
                _ => Path.Combine(authoringHome.Root, "tap.dll")
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, "\nmutation"); Changed = true;
            return new([], []); // Injection proves boundary enforcement; no external process evidence.
        }
    }
}
