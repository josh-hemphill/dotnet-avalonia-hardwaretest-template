using HardwareTest.Core.Settings;

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
            settingsStore.AppSettingsSaved += (_, _) =>
            {
                IsEngineerDebugMode = settingsStore.AppSettings.IsEngineerDebugMode;
            };
        }
    }
}
