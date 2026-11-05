using System.Diagnostics;
using System.Text.Json;
using HardwareTest.Authoring;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.Tests;

/// Executes generated shell helpers against a recording protocol stub. This
/// verifies argument boundaries and old-client refusal, not runtime installation.
public sealed class OfflineBenchHelperProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "offline-helper-protocol-" + Guid.NewGuid().ToString("N"));
    private const string Build = "1.2.3+helper.build-7";
    private string Bundle => Path.Combine(_root, "bundle with 'quotes' and $literal");
    private string Release => Path.Combine(_root, "release with spaces and 'quotes'");
    private string Operator => Path.Combine(Release, "HardwareTest");
    private string Log => Path.Combine(_root, "operator-arguments.log");

    public OfflineBenchHelperProtocolTests()
    {
        Directory.CreateDirectory(Bundle); Directory.CreateDirectory(Release);
        OfflineBenchBundleHelpers.Write(Bundle, new("Linux", "x64"));
        var manifest = new OfflineBenchBundleManifest { TargetOS = "Linux", TargetArchitecture = "x64", RequiredOperatorBuild = Build };
        var json = JsonSerializer.Serialize(manifest, OfflineBenchJsonContext.Default.OfflineBenchBundleManifest);
        Assert.Contains(@"\u002B", json);
        File.WriteAllText(Path.Combine(Bundle, OfflineBenchBundle.ManifestFileName), json);
        File.WriteAllText(Path.Combine(Release, "HardwareTest.dll"), "protocol operator identity");
        File.WriteAllText(Path.Combine(Release, "HardwareTest.OpenTap.Host.dll"), "protocol host identity");
        File.WriteAllText(Operator, """
            #!/bin/sh
            set -eu
            printf 'CALL:%s\n' "$#" >> "$HT_OFFLINE_PROTOCOL_LOG"
            printf '<%s>\n' "$@" >> "$HT_OFFLINE_PROTOCOL_LOG"
            if [ "$1" = --version ]; then printf '%s\n' "$HT_OFFLINE_PROTOCOL_VERSION"; fi
            """ + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Operator, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task Generated_check_helper_decodes_the_serialized_plus_and_preserves_the_bundle_argument()
    {
        if (!OperatingSystem.IsLinux()) return;
        WriteCapability();
        var result = await Run("check-offline-bundle.sh", Operator);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(["CALL:1", "<--version>", "CALL:2", "<--check-offline-bundle>", "<" + Bundle + ">"], File.ReadAllLines(Log));
    }

    [Fact]
    public async Task Generated_install_helper_checks_first_and_passes_quoted_fresh_app_and_data_as_distinct_arguments()
    {
        if (!OperatingSystem.IsLinux()) return;
        WriteCapability();
        var destination = Path.Combine(_root, "new app 'quoted' $literal");
        var data = Path.Combine(_root, "fresh data; $(touch SHOULD-NOT-EXIST)");
        var result = await Run("install-offline-bundle.sh", Operator, destination, data);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal([
            "CALL:1", "<--version>",
            "CALL:2", "<--check-offline-bundle>", "<" + Bundle + ">",
            "CALL:6", "<--install-offline-bundle>", "<" + Bundle + ">", "<--destination>", "<" + destination + ">", "<--data-directory>", "<" + data + ">"
        ], File.ReadAllLines(Log));
        Assert.False(Directory.Exists(destination)); Assert.False(Directory.Exists(data));
        Assert.False(File.Exists(Path.Combine(_root, "SHOULD-NOT-EXIST")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Older_or_changed_operator_is_refused_before_any_executable_invocation(bool changedAfterReceipt)
    {
        if (!OperatingSystem.IsLinux()) return;
        if (changedAfterReceipt)
        {
            WriteCapability(); File.AppendAllText(Path.Combine(Release, "HardwareTest.dll"), "changed after receipt");
        }
        var result = await Run("check-offline-bundle.sh", Operator);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(changedAfterReceipt ? "integrity check failed" : "no offline-bench capability receipt", result.Output);
        Assert.False(File.Exists(Log));
    }

    [Fact]
    public async Task Other_json_escapes_in_build_identity_are_refused_before_operator_invocation()
    {
        if (!OperatingSystem.IsLinux()) return;
        WriteCapability();
        var path = Path.Combine(Bundle, OfflineBenchBundle.ManifestFileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace(@"\u002B", @"\u0024", StringComparison.Ordinal));
        var result = await Run("check-offline-bundle.sh", Operator);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("build identity is missing", result.Output);
        Assert.False(File.Exists(Log));
    }

    private void WriteCapability()
    {
        var receipt = new OfflineBenchCapabilityReceipt
        {
            OperatorSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(Path.Combine(Release, "HardwareTest.dll"))),
            HostSha256 = BenchProgramHandoff.Hash(File.ReadAllBytes(Path.Combine(Release, "HardwareTest.OpenTap.Host.dll"))),
            RuntimeIdentifier = "linux-x64",
        };
        File.WriteAllText(Path.Combine(Release, "offline-bench-capability.json"), JsonSerializer.Serialize(receipt, OfflineBenchJsonContext.Default.OfflineBenchCapabilityReceipt));
    }

    private async Task<(int ExitCode, string Output)> Run(string helper, params string[] args)
    {
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _root };
        start.ArgumentList.Add(Path.Combine(Bundle, helper));
        foreach (var argument in args) start.ArgumentList.Add(argument);
        start.Environment["HT_OFFLINE_PROTOCOL_LOG"] = Log;
        start.Environment["HT_OFFLINE_PROTOCOL_VERSION"] = Build;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        return (process.ExitCode, await stdout + await stderr);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
