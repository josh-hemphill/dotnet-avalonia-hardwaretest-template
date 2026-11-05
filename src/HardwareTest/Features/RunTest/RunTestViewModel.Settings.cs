using HardwareTest.Core.Settings;
using HardwareTest.UiThreading;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    /// Applies live settings and keeps engineer-debug in sync after save.
    private void BindLiveSettings(ISettingsStore? settingsStore, AppSettings settings)
    {
        Status = "Confirm DUT, then Run.";
        IsEngineerDebugMode = settings.IsEngineerDebugMode;
        if (settingsStore is not null)
        {
            settingsStore.AppSettingsSaved += (_, _) => UiDispatch.Post(() =>
            {
                IsEngineerDebugMode = settingsStore.AppSettings.IsEngineerDebugMode;
                if (SessionPanel is { } panel)
                {
                    panel.RefreshRequirementFlags();
                    panel.RefreshSessionSummary();
                }
                RefreshVisaMode();
                RefreshStationHealthGate();
            }, UiScheduler);
        }
    }
}
