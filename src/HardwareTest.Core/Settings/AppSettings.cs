namespace HardwareTest.Core.Settings;

/// Application preferences persisted to settings.json.
public sealed class AppSettings
{
    /// Persisted document schema version (see SchemaVersions.AppSettings).
    public int SchemaVersion { get; set; }
    public string DataDirectory { get; set; } = string.Empty;
    public string DefaultVisaResource { get; set; } = "MOCK::INSTR0";
    public bool UseMockVisa { get; set; } = true;
    public string LogMinimumLevel { get; set; } = "Information";
    public bool EnableOsEventSink { get; set; } = false;
    public bool EnableSyslogOnUnix { get; set; } = false;
    public string? SyslogHost { get; set; } = "127.0.0.1";
    public int SyslogPort { get; set; } = 514;
    public int PlotRefreshHz { get; set; } = 20;
    /// System, Light, or Dark.
    public string ThemePreference { get; set; } = "System";
    public bool EmbedPlotsInReport { get; set; } = true;
    /// When true, attach a ResultListener that writes OpenTAP tables as CSV under runs/{runId}/opentap-results/.
    public bool ExportOpenTapResults { get; set; }
    /// When true, show DUT history summary on the Run board after Pass/Fail (default off; use Results).
    public bool ShowDutHistoryOnRun { get; set; }
    /// Canonical idle window in minutes before Operator Session becomes Stale (default 240 = 4h).
    public int OperatorSessionIdleMinutes { get; set; } = 240;
    /// Compatibility alias for idle window in hours (env/CLI / older settings.json). Prefer minutes.
    public int OperatorSessionIdleHours { get; set; } = 4;
    /// Soft-warn when idle elapsed reaches this percent of the idle window (50–95, default 80).
    public int OperatorSessionIdleWarnPercent { get; set; } = 80;
    /// When true, each terminal run marks the session Stale so the next Run requires Same DUT / Change Session.
    public bool RequireDutConfirmEveryRun { get; set; }
    /// When true, Run page exposes constrained on-bench debug edits.
    public bool IsEngineerDebugMode { get; set; }
    /// Extra OpenTAP plugin search directories (Basic plugins are always included).
    public List<string> OpenTapPluginDirectories { get; set; } = [];
    /// Embedded Typst template file name (override via DataDirectory/reports/{name}).
    public string ReportTemplateName { get; set; } = "test-report.typ";
    public List<VisaInstrument> Instruments { get; set; } = [];
    /// Station overlay: logical role → registry instrument Id (bench-specific). Kept for migration; prefer PlanSlotOverrides.
    public List<StationBinding> StationBindings { get; set; } = [];
    /// Per-plan OpenTAP slot → VISA resource overrides (station overlay).
    public List<PlanSlotOverride> PlanSlotOverrides { get; set; } = [];
    /// Per-plan OpenTAP parameter overrides (station overlay; does not mutate TapPlan files).
    public List<PlanParameterOverride> PlanParameterOverrides { get; set; } = [];
    /// When false, crash capture surfaces are no-ops.
    public bool CrashEnabled { get; set; } = true;
    /// Crash dossier root; empty means {DataDirectory}/crashes.
    public string CrashDirectory { get; set; } = string.Empty;
    /// Max retained crash dossiers (newest kept).
    public int CrashRetentionCount { get; set; } = 20;
    /// When true, DUT serial / operator names are hashed in diagnostics and crash dossiers.
    public bool RedactIdentifiersInDiagnostics { get; set; } = true;
    /// Fixed share / docked export path (empty = none). Prefer removable when PreferRemovableExport.
    public string ExportDirectory { get; set; } = string.Empty;
    /// When true, list detected removable roots ahead of ExportDirectory.
    public bool PreferRemovableExport { get; set; } = true;
    /// Delete completed run folders older than N days (0 = age retention off).
    public int RunRetentionDays { get; set; } = 30;
    /// Keep at most N newest completed run folders (0 = count retention off).
    public int RunRetentionMaxRuns { get; set; } = 500;
    /// Soft-warn free space on DataDirectory volume before Run (bytes).
    public long DataFreeSpaceWarnBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    /// Block new Run when free space is at or below this (bytes).
    public long DataFreeSpaceCriticalBytes { get; set; } = 512L * 1024 * 1024;
    /// When true, Settings/Home may open OS folders (Explorer). Appliance profiles set false.
    public bool AllowOsFolderBrowse { get; set; } = true;
    /// Milliseconds to wait after Abort before killing the OpenTAP worker (clamped 1000–120000).
    public int OpenTapWorkerKillTimeoutMilliseconds { get; set; } = DefaultOpenTapWorkerKillTimeoutMilliseconds;
    /// Soft-warn when |local − NTP| or a backward last-known-good jump exceeds this many minutes.
    public int ClockSkewWarnThresholdMinutes { get; set; } = DefaultClockSkewWarnThresholdMinutes;
    /// Optional local NTP / domain time host. Empty skips the NTP query (last-known-good only).
    public string NtpHost { get; set; } = string.Empty;
    /// When true, chip/tap uses the in-process mock (CI / no reader).
    public bool UseMockOperatorCredential { get; set; } = true;
    /// When true, confirming a session that requires an operator also requires a chip or tap.
    public bool RequireCredentialForOperator { get; set; }
    /// When true, exporting or opening a certification PDF requires a badge attestation.
    public bool RequireAttestationBeforeExport { get; set; }
    /// When true, a presence stamp is accepted only as a site-policy fallback if on-card signing cannot be used.
    public bool AllowPresenceInLieuOfSigning { get; set; } = true;

    public const int DefaultOpenTapWorkerKillTimeoutMilliseconds = 8000;
    public const int MinOpenTapWorkerKillTimeoutMilliseconds = 1000;
    public const int MaxOpenTapWorkerKillTimeoutMilliseconds = 120_000;
    public const int DefaultClockSkewWarnThresholdMinutes = 5;
    public const int MinClockSkewWarnThresholdMinutes = 1;
    public const int MaxClockSkewWarnThresholdMinutes = 1440;
}

/// Named VISA instrument entry in the persisted registry (legacy; Instruments UI no longer edits this).
public sealed class VisaInstrument
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }
}

/// Maps a suite/plan role (e.g. dmm) to a registry instrument Id for this station.
public sealed class StationBinding
{
    public string Role { get; set; } = string.Empty;
    public string InstrumentId { get; set; } = string.Empty;
}

/// Overrides an OpenTAP instrument slot resource for a specific plan on this station.
public sealed class PlanSlotOverride
{
    public string PlanId { get; set; } = string.Empty;
    public string SlotName { get; set; } = string.Empty;
    public string RoleHint { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
}

/// Overrides an OpenTAP plan/step parameter for a specific plan on this station.
public sealed class PlanParameterOverride
{
    public string PlanId { get; set; } = string.Empty;
    public string MemberKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// Window and navigation state persisted to ui-state.json.
public sealed class UiState
{
    /// Persisted document schema version (see SchemaVersions.UiState).
    public int SchemaVersion { get; set; }
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public double NormalX { get; set; } = 100;
    public double NormalY { get; set; } = 100;
    public double NormalWidth { get; set; } = 1280;
    public double NormalHeight { get; set; } = 800;
    public bool IsMaximized { get; set; }
    public string SelectedPageId { get; set; } = "Home";
    /// Avalonia screen DeviceName last used for this window.
    public string? MonitorDeviceName { get; set; }
    public bool CompactStepRows { get; set; }
}
