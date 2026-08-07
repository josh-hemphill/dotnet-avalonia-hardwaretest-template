using HardwareTest.Features.Shell;

namespace HardwareTest.Features.RunTest;

/// Mirrors Run/storage/history severity into the shell strip (Phase 17). Host props remain the
/// pipeline contract; the strip is the only presentation chrome.
public partial class RunTestViewModel
{
    private void PublishRunBannerToShell(RunBannerSeverity severity, string message)
    {
        _shellNotification?.Publish(
            ShellNotificationBrushConverter.FromRun(severity),
            message,
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceRun,
            onDismissed: ClearLocalRunBanner);
    }

    private void ClearRunBannerFromShell()
        => _shellNotification?.Clear(ShellNotificationViewModel.SourceRun);

    private void ClearLocalRunBanner()
    {
        HasBanner = false;
        BannerMessage = string.Empty;
    }

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
}
