using HardwareTest.Features.Shell;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    private void PublishRunBannerToShell(RunBannerSeverity severity, string message)
    {
        _shellNotification?.Publish(
            MapSeverity(severity),
            message,
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceRun,
            onDismissed: () =>
            {
                HasBanner = false;
                BannerMessage = string.Empty;
            });
    }

    private void ClearRunBannerFromShell()
        => _shellNotification?.Clear(ShellNotificationViewModel.SourceRun);

    private void PublishStorageToShell()
    {
        if (_shellNotification is null)
        {
            return;
        }

        if (!HasStorageBanner || string.IsNullOrWhiteSpace(StorageBannerMessage))
        {
            _shellNotification.Clear(ShellNotificationViewModel.SourceStorage);
            return;
        }

        _shellNotification.Publish(
            StorageBannerIsCritical
                ? ShellNotificationSeverity.Critical
                : ShellNotificationSeverity.Warning,
            StorageBannerMessage,
            dismissible: !StorageBannerIsCritical,
            sourceKey: ShellNotificationViewModel.SourceStorage,
            onDismissed: StorageBannerIsCritical
                ? null
                : () =>
                {
                    StorageBannerDismissed = true;
                    HasStorageBanner = false;
                });
    }

    private void ClearStorageFromShell()
        => _shellNotification?.Clear(ShellNotificationViewModel.SourceStorage);

    private void SyncHistoryBannerToShell(string? message)
    {
        if (_shellNotification is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            _shellNotification.Clear(ShellNotificationViewModel.SourceHistory);
            return;
        }

        _shellNotification.Publish(
            ShellNotificationSeverity.Info,
            message,
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceHistory);
    }

    private static ShellNotificationSeverity MapSeverity(RunBannerSeverity severity) => severity switch
    {
        RunBannerSeverity.Error => ShellNotificationSeverity.Error,
        RunBannerSeverity.Warning => ShellNotificationSeverity.Warning,
        _ => ShellNotificationSeverity.Info,
    };
}
