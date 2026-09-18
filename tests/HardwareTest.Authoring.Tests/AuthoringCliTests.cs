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
        Assert.Contains("--eval-formulas", output.ToString(), StringComparison.Ordinal);
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
    public void Compat_without_workspace_is_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--compat"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
    }

    [Fact]
    public void Eval_formulas_without_workspace_is_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--eval-formulas"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
    }

    [Fact]
    public void Unknown_flag_is_usage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--nope"], output, error);
        Assert.Equal(AuthoringCli.UsageExitCode, code);
    }

    [Fact]
    public void ResolveOpenTapHome_prefers_cli_flag_then_prefs_and_fail_closes()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "ht-cli-prefs-" + Guid.NewGuid().ToString("N"),
            AuthoringPreferencesStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new AuthoringPreferencesStore(path);
        store.Load();
        store.Current.OpenTapHomeOverride = "/opt/prefs-home";
        store.Save();

        Assert.Equal("/cli-home", AuthoringCli.ResolveOpenTapHome("/cli-home", store));
        Assert.Equal("/opt/prefs-home", AuthoringCli.ResolveOpenTapHome(null, store));
        Assert.Equal("/opt/prefs-home", AuthoringCli.ResolveOpenTapHome("  ", store));

        File.WriteAllText(path, "{");
        var broken = new AuthoringPreferencesStore(path);
        Assert.Null(AuthoringCli.ResolveOpenTapHome(null, new ThrowingPreferencesStore()));
        Assert.Throws<AuthoringPreferencesException>(broken.Load);
        Assert.Null(AuthoringCli.ResolveOpenTapHome(null, broken));
    }

    private sealed class ThrowingPreferencesStore : IAuthoringPreferencesStore
    {
        public AuthoringPreferences Current => throw new AuthoringPreferencesException("boom");

        public bool IsReadOnly => false;

        public string? Warning => null;

        public string FilePath => "throwing";

        public void Load() => throw new AuthoringPreferencesException("boom");

        public void Save() => throw new AuthoringPreferencesException("boom");
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

    [Fact]
    public void Compat_template_workspace_succeeds()
    {
        var workspaceRoot = CopyTemplateWorkspace();
        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(
            ["--compat", workspaceRoot, "--opentap-home", home.Root, "--offline"],
            output,
            error);
        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()), error.ToString());
        Assert.Contains("TUI compatibility ok", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Eval_formulas_template_with_empty_recordings_succeeds()
    {
        var root = Path.Combine(FindRepoRoot(), "plans", "opentap");
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AuthoringCli.Run(["--eval-formulas", root], output, error);
        Assert.Equal(0, code);
        Assert.True(string.IsNullOrWhiteSpace(error.ToString()), error.ToString());
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
