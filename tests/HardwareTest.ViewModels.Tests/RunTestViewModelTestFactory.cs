using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Shell;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;

namespace HardwareTest.ViewModels.Tests;

/// Shared Run board construction for Phase chrome / ViewModel tests.
internal static class RunTestViewModelTestFactory
{
    public static RunTestViewModel Create(
        FakeOpenTapSession? openTap = null,
        AppSettings? settings = null,
        FakeRunControl? runControl = null,
        IStorageHealthService? storageHealth = null,
        ShellNotificationViewModel? shellNotification = null,
        IDutHistoryService? dutHistory = null)
    {
        openTap ??= new FakeOpenTapSession();
        runControl ??= new FakeRunControl();
        return new RunTestViewModel(
            openTap,
            openTap,
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            new FakeRunStore(),
            settings ?? new AppSettings(),
            dutHistory: dutHistory,
            storageHealth: storageHealth,
            shellNotification: shellNotification);
    }
}
