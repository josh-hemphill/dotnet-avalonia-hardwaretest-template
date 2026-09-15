namespace HardwareTest;

/// Explicit entry for a launch-time shell-app package (`shell-app.json`).
public sealed class ShellApplicationPackageManifest
{
    public required string Id { get; init; }
    public required string Assembly { get; init; }
    public required string Type { get; init; }
}
