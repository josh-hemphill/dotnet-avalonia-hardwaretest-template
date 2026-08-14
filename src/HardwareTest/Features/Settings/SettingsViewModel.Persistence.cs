using System;
using System.Globalization;
using System.Threading.Tasks;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Settings;

namespace HardwareTest.Features.Settings;

public partial class SettingsViewModel
{
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await SaveCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveCoreAsync()
    {
        _operatorSession?.TouchActivity();
        var s = _settingsStore.AppSettings;
        string? visaApplyStatus = null;
        var visaRefused = false;
        if (!UseMockVisaReadOnly)
        {
            if (_visaModeController is not null && UseMockVisa != _visaModeController.EffectiveUseMockVisa)
            {
                if (_visaModeController.TryApply(UseMockVisa, out var applyMsg))
                {
                    visaApplyStatus = applyMsg;
                    s.UseMockVisa = UseMockVisa;
                }
                else
                {
                    visaRefused = true;
                    visaApplyStatus = applyMsg;
                    var effective = _visaModeController.EffectiveUseMockVisa;
                    s.UseMockVisa = effective;
                    UseMockVisa = effective;
                }
            }
            else
            {
                s.UseMockVisa = UseMockVisa;
            }
        }

        if (!LogMinimumLevelReadOnly)
        {
            s.LogMinimumLevel = NormalizeLogLevel(LogMinimumLevel);
        }

        if (!EnableOsEventSinkReadOnly)
        {
            s.EnableOsEventSink = EnableOsEventSink;
        }

        if (!EnableSyslogOnUnixReadOnly)
        {
            s.EnableSyslogOnUnix = EnableSyslogOnUnix;
        }

        if (!SyslogHostReadOnly)
        {
            s.SyslogHost = SyslogHost;
        }

        if (!SyslogPortReadOnly)
        {
            s.SyslogPort = SyslogPort;
        }

        if (!PlotRefreshHzReadOnly)
        {
            s.PlotRefreshHz = PlotRefreshHz;
        }

        if (!ThemePreferenceReadOnly)
        {
            s.ThemePreference = ThemePreference;
        }

        if (!EmbedPlotsInReportReadOnly)
        {
            s.EmbedPlotsInReport = EmbedPlotsInReport;
        }

        if (!ExportOpenTapResultsReadOnly)
        {
            s.ExportOpenTapResults = ExportOpenTapResults;
        }

        if (!ShowDutHistoryOnRunReadOnly)
        {
            s.ShowDutHistoryOnRun = ShowDutHistoryOnRun;
        }

        if (!IsEngineerDebugModeReadOnly)
        {
            s.IsEngineerDebugMode = IsEngineerDebugMode;
        }

        if (!OperatorSessionIdleMinutesReadOnly)
        {
            s.OperatorSessionIdleMinutes = OperatorSessionIdle.ClampMinutes(OperatorSessionIdleMinutes);
            s.OperatorSessionIdleHours = OperatorSessionIdle.MinutesToHoursDisplay(s.OperatorSessionIdleMinutes);
        }

        if (!OperatorSessionIdleWarnPercentReadOnly)
        {
            s.OperatorSessionIdleWarnPercent =
                OperatorSessionIdle.ClampWarnPercent(OperatorSessionIdleWarnPercent);
        }

        if (!RequireDutConfirmEveryRunReadOnly)
        {
            s.RequireDutConfirmEveryRun = RequireDutConfirmEveryRun;
        }

        if (!RunRetentionDaysReadOnly)
        {
            s.RunRetentionDays = Math.Max(0, RunRetentionDays);
        }

        if (!RunRetentionMaxRunsReadOnly)
        {
            s.RunRetentionMaxRuns = Math.Max(0, RunRetentionMaxRuns);
        }

        if (!ExportDirectoryReadOnly)
        {
            s.ExportDirectory = ExportDirectory?.Trim() ?? string.Empty;
        }

        if (!DataFreeSpaceWarnGbReadOnly)
        {
            s.DataFreeSpaceWarnBytes = GbToBytes(DataFreeSpaceWarnGb);
        }

        if (!DataFreeSpaceCriticalGbReadOnly)
        {
            s.DataFreeSpaceCriticalBytes = GbToBytes(DataFreeSpaceCriticalGb);
        }

        await _settingsStore.SaveAppSettingsAsync();
        ThemeApplier.Apply(s);
        AllowOsFolderBrowse = s.AllowOsFolderBrowse || s.IsEngineerDebugMode;
        RefreshProvenance();
        if (!_settingsStore.IsSettingsWritable)
        {
            Status = $"Could not write settings.json ({_settingsStore.LastPersistenceError}). Continuing in memory.";
            return;
        }

        if (visaRefused && visaApplyStatus is not null)
        {
            Status = visaApplyStatus;
        }
        else if (visaApplyStatus is not null)
        {
            Status = $"Saved at {FormatSavedAt()}. {visaApplyStatus}";
        }
        else
        {
            Status = $"Saved at {FormatSavedAt()}. Restart may be required for logging sink changes.";
        }
    }

    /// Whole seconds only — culture `T` can include fractional seconds and reflow the sticky Save row.
    internal static string FormatSavedAt(DateTimeOffset? now = null)
        => (now ?? DateTimeOffset.Now).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static double BytesToGb(long bytes)
        => Math.Round(bytes / (1024d * 1024d * 1024d), 2, MidpointRounding.AwayFromZero);

    private static long GbToBytes(double gb)
        => (long)Math.Round(Math.Max(0, gb) * 1024d * 1024d * 1024d, MidpointRounding.AwayFromZero);

    private bool IsPropertyOverridden(string? propertyName)
        => propertyName switch
        {
            nameof(UseMockVisa) => UseMockVisaReadOnly,
            nameof(LogMinimumLevel) => LogMinimumLevelReadOnly,
            nameof(EnableOsEventSink) => EnableOsEventSinkReadOnly,
            nameof(EnableSyslogOnUnix) => EnableSyslogOnUnixReadOnly,
            nameof(SyslogHost) => SyslogHostReadOnly,
            nameof(SyslogPort) => SyslogPortReadOnly,
            nameof(PlotRefreshHz) => PlotRefreshHzReadOnly,
            nameof(ThemePreference) => ThemePreferenceReadOnly,
            nameof(EmbedPlotsInReport) => EmbedPlotsInReportReadOnly,
            nameof(ExportOpenTapResults) => ExportOpenTapResultsReadOnly,
            nameof(ShowDutHistoryOnRun) => ShowDutHistoryOnRunReadOnly,
            nameof(IsEngineerDebugMode) => IsEngineerDebugModeReadOnly,
            nameof(OperatorSessionIdleMinutes) => OperatorSessionIdleMinutesReadOnly,
            nameof(OperatorSessionIdleWarnPercent) => OperatorSessionIdleWarnPercentReadOnly,
            nameof(RequireDutConfirmEveryRun) => RequireDutConfirmEveryRunReadOnly,
            nameof(RunRetentionDays) => RunRetentionDaysReadOnly,
            nameof(RunRetentionMaxRuns) => RunRetentionMaxRunsReadOnly,
            nameof(ExportDirectory) => ExportDirectoryReadOnly,
            nameof(DataFreeSpaceWarnGb) => DataFreeSpaceWarnGbReadOnly,
            nameof(DataFreeSpaceCriticalGb) => DataFreeSpaceCriticalGbReadOnly,
            _ => false,
        };

    private static string NormalizeLogLevel(string? level)
    {
        var value = string.IsNullOrWhiteSpace(level) ? "Information" : level.Trim();
        return value switch
        {
            "Verbose" or "Debug" or "Information" or "Warning" or "Error" or "Fatal" => value,
            _ => "Information",
        };
    }
}
