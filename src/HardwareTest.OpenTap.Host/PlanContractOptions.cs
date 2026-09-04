using HardwareTest.Core.Settings;

namespace HardwareTest.OpenTap.Host;

public enum PlanContractFormat
{
    Text,
    Json,
    Sarif,
}

/// Options for authoring / pack-gate validation. Operator Run does not use this.
public sealed class PlanContractOptions
{
    public AppSettings? Settings { get; init; }

    /// Trust <see cref="AppSettings.OpenTapPluginDirectories"/> (CLI) for this process.
    public bool TrustConfiguredPluginDirectories { get; init; }

    /// Missing sidecar is an error (pack/CI). Ad-hoc authoring keeps a warning.
    public bool Strict { get; init; }

    public PlanContractFormat Format { get; init; } = PlanContractFormat.Text;
}
