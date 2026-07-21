namespace HardwareTest.Core.Settings;

/// Application preferences persisted to settings.json.
public sealed class AppSettings
{
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
    /// Idle hours before Operator Session becomes Stale (soft re-confirm).
    public int OperatorSessionIdleHours { get; set; } = 4;
    /// When true, Run page exposes constrained on-bench debug edits.
    public bool IsEngineerDebugMode { get; set; }
    public List<VisaInstrument> Instruments { get; set; } = [];
    /// Station overlay: logical role → registry instrument Id (bench-specific). Kept for migration; prefer PlanSlotOverrides.
    public List<StationBinding> StationBindings { get; set; } = [];
    /// Per-plan OpenTAP slot → VISA resource overrides (station overlay).
    public List<PlanSlotOverride> PlanSlotOverrides { get; set; } = [];
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

/// Window and navigation state persisted to ui-state.json.
public sealed class UiState
{
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
