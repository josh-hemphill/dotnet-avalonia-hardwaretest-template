using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringManifestCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-manifest-cli-" + Guid.NewGuid().ToString("N"));
    public AuthoringManifestCliTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public void Removed_migration_command_reports_usage_without_changing_manifest(int version)
    {
        var path = Path.Combine(_root, "authoring.json");
        File.WriteAllText(path, $"{{\"schemaVersion\":{version},\"plansDirectory\":\".\"}}\r\n");
        var original = File.ReadAllBytes(path);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(AuthoringCli.UsageExitCode, AuthoringCli.Run(["--migrate", _root], output, error));
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("--migrate", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("--bootstrap")]
    [InlineData("--validate")]
    [InlineData("--pack")]
    [InlineData("--compat")]
    public void Older_manifest_is_rejected_by_commands_without_creating_artifacts(string command)
    {
        var path = Path.Combine(_root, "authoring.json");
        File.WriteAllText(path, """{"schemaVersion":1,"plansDirectory":"."}""" + "\r\n");
        var original = File.ReadAllBytes(path);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var arguments = command == "--pack"
            ? new[] { command, _root, "--out", Path.Combine(_root, "dist") }
            : new[] { command, _root };
        Assert.Equal(1, AuthoringCli.Run(arguments, output, error));
        Assert.Contains("requires schema 2", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
