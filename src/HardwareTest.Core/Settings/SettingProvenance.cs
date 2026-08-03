namespace HardwareTest.Core.Settings;

/// Where an effective setting value came from (low → high precedence).
public enum SettingSource
{
    Default = 0,
    SettingsFile = 1,
    Environment = 2,
    CommandLine = 3,
}

/// Provenance for one effective AppSettings value.
public sealed class SettingProvenance
{
    public required string Key { get; init; }
    public required string EffectiveValue { get; init; }
    public required SettingSource Source { get; init; }
    public string? RawValue { get; init; }
    public string? SourceDetail { get; init; }
}
