using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringCliTests
{
    [Fact]
    public void Headless_flags_do_not_include_bare_workspace_path()
    {
        Assert.False(AuthoringCli.IsHeadless(["plans/opentap"]));
        Assert.True(AuthoringCli.IsHeadless(["--help"]));
        Assert.True(AuthoringCli.IsHeadless(["--pack", "plans/opentap", "--out", "dist"]));
        Assert.True(AuthoringCli.IsHeadless(["--validate", "plans/opentap"]));
    }

    [Fact]
    public void Help_prints_usage_and_exits_2()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--help"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
        Assert.Contains("HardwareTest.Authoring --pack", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--bootstrap", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--compat", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_without_out_is_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--pack", "plans/opentap"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
        Assert.Contains("--out", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Compat_exits_1_until_checker_exists()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--compat", "plans/opentap"], output, error);
        Assert.Equal(1, code);
        Assert.Contains("not available", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_flag_is_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--nope"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
    }
}

[Collection("AuthoringOpenTap")]
public sealed class AuthoringCliOpenTapTests
{
    [Fact]
    public void Validate_template_workspace_succeeds()
    {
        var root = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--validate", root, "--strict"], output, error);
        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()), error.ToString());
    }

    [Fact]
    public void Pack_writes_ship_manifest_without_avalonia()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });
        var dist = NewTempDir();
        var output = new StringWriter();
        var error = new StringWriter();

        var code = AuthoringCli.Run(
            ["--pack", workspaceRoot, "--out", dist, "--opentap-home", home.Root, "--offline"],
            output,
            error);

        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()), error.ToString());
        Assert.Contains("HardwareTest Template Program", output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(dist, WorkspacePacker.ShipManifestFileName)));
    }

    private static string CopyTemplateWorkspace()
    {
        var src = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var dest = NewTempDir();
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        }

        return dest;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-authoring-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("HardwareTest.slnx").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate HardwareTest.slnx above '{AppContext.BaseDirectory}'.");
    }
}
