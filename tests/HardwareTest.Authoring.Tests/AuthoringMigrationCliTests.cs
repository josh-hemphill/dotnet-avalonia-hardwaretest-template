using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringMigrationCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-migration-cli-" + Guid.NewGuid().ToString("N"));
    public AuthoringMigrationCliTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Migration_command_retains_original_manifest_and_is_repeatable()
    {
        var path = Path.Combine(_root, "authoring.json");
        var original = """{"schemaVersion":1,"displayName":"legacy","plansDirectory":"."}""";
        File.WriteAllText(path, original);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, AuthoringCli.Run(["--migrate", _root], output, error));
        Assert.Equal(original, File.ReadAllText(path + ".schema-1.bak"));
        Assert.Equal(AuthoringSchemaVersions.Manifest, AuthoringWorkspaceLoader.Load(_root).Manifest.SchemaVersion);
        var migrated = File.ReadAllBytes(path);
        Assert.Equal(0, AuthoringCli.Run(["--migrate", _root], output, error));
        Assert.Equal(migrated, File.ReadAllBytes(path));
        Assert.Contains("no changes", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Migration_command_preserves_future_manifest_bytes_and_reports_read_only()
    {
        var path = Path.Combine(_root, "authoring.json");
        var original = """{"schemaVersion":999,"displayName":"future","plansDirectory":".","newSetting":true}""";
        File.WriteAllText(path, original);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, AuthoringCli.Run(["--migrate", _root], output, error));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Contains("future", error.ToString());
        Assert.False(File.Exists(path + ".schema-999.bak"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
