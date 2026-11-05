using System.IO.Compression;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Storage;
using HardwareTest.Features.Home;
using HardwareTest.Features.Shell;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class HomeSupportWorkflowTests
{
    [Fact]
    public async Task Technician_exports_real_support_zip_through_notification_when_folder_browsing_is_disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "persona-support-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new FakeSettingsStore(root);
            settings.AppSettings.AllowOsFolderBrowse = false;
            settings.AppSettings.IsEngineerDebugMode = false;
            settings.AppSettings.ExportDirectory = Path.Combine(root, "support-delivery");
            var writer = CrashDossierWriter.FromSettings(settings.AppSettings, root);
            var dossier = writer.TryWrite(new CrashCaptureContext
            {
                Report = new CrashReportDocument
                {
                    DossierId = Guid.NewGuid().ToString("N"),
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    Source = "persona-maintenance-test",
                    IsFatal = true,
                },
                LogTail = "Mock station maintenance evidence",
                Config = new CrashConfigSnapshot(),
                Session = new CrashSessionSnapshot(),
            });
            Assert.NotNull(dossier);
            var notification = new ShellNotificationViewModel();
            var targets = new ExportTargetService(settings.AppSettings, root, removableRoots: () => []);
            var home = new HomeViewModel(settings, targets, notification);
            Assert.True(home.HasCrashBanner);
            Assert.False(home.AllowOsFolderBrowse);
            Assert.Null(notification.SecondaryCommand);
            Assert.Equal("Export support bundle", notification.PrimaryLabel);
            await home.OpenCrashFolderCommand.ExecuteAsync();
            Assert.Contains("disabled", home.CrashStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Same(home.ExportSupportBundleCommand, notification.PrimaryCommand);
            await home.ExportSupportBundleCommand.ExecuteAsync();
            var zip = Assert.Single(Directory.GetFiles(settings.AppSettings.ExportDirectory, "*.zip"));
            Assert.Contains(zip, home.CrashStatus, StringComparison.Ordinal);
            using var contents = ZipFile.OpenRead(zip);
            foreach (var name in new[] { "crash.json", "log-tail.txt", "config.json", "session.json" })
                Assert.Contains(contents.Entries, entry => entry.FullName == name);
            using var log = new StreamReader(contents.GetEntry("log-tail.txt")!.Open());
            Assert.Contains("maintenance evidence", await log.ReadToEndAsync(), StringComparison.Ordinal);
            Assert.True(Directory.Exists(dossier));
            Assert.False(File.Exists(Path.Combine(dossier, CrashDossierWriter.ReviewedMarkerFileName)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
