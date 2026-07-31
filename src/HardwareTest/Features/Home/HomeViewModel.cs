using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Settings;
using HardwareTest.Crash;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Home;

public partial class HomeViewModel : ReactiveObject
{
    private readonly ISettingsStore? _settingsStore;
    private readonly CrashDossierWriter? _writer;

    public HomeViewModel()
        : this(null)
    {
    }

    public HomeViewModel(ISettingsStore? settingsStore)
    {
        _settingsStore = settingsStore;
        if (settingsStore is not null)
        {
            _writer = CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory);
        }

        OpenCrashFolderCommand = ReactiveCommand.Create(OpenCrashFolder);
        ExportSupportBundleCommand = ReactiveCommand.Create(ExportSupportBundle);
        DismissCrashBannerCommand = ReactiveCommand.Create(DismissCrashBanner);
        RefreshCrashBanner();
        CrashHandler.RecoverableCrashOccurred += (_, _) => RefreshCrashBanner();
    }

    public string Title { get; } = "Hardware Test";

    public string Summary { get; } =
        "Confirm a DUT once, run locked OpenTAP programs from Avalonia, manage station instruments, and publish Typst reports with live plots when needed.";

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCrashFolderCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ExportSupportBundleCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> DismissCrashBannerCommand { get; }

    [Reactive] private bool _hasCrashBanner;
    [Reactive] private string _crashBannerTitle = string.Empty;
    [Reactive] private string _crashBannerDetail = string.Empty;
    [Reactive] private string _crashStatus = string.Empty;

    private CrashDossierSummary? _activeDossier;

    public void RefreshCrashBanner()
    {
        try
        {
            var unreviewed = _writer?.ListUnreviewed() ?? [];
            _activeDossier = unreviewed.FirstOrDefault();
            if (_activeDossier is null)
            {
                if (!string.IsNullOrWhiteSpace(CrashHandler.LastRecoverableMessage))
                {
                    HasCrashBanner = true;
                    CrashBannerTitle = "Recoverable fault captured";
                    CrashBannerDetail = CrashHandler.LastRecoverableMessage!;
                    return;
                }

                HasCrashBanner = false;
                CrashBannerTitle = string.Empty;
                CrashBannerDetail = string.Empty;
                return;
            }

            HasCrashBanner = true;
            CrashBannerTitle = _activeDossier.IsFatal ? "Previous session ended unexpectedly" : "A recoverable fault was captured";
            var when = _activeDossier.CapturedAtUtc.ToString("u");
            var ver = _activeDossier.AppVersion ?? "unknown";
            var fault = _activeDossier.ExceptionType ?? "Exception";
            CrashBannerDetail = $"{when} — {fault} — app {ver}. Open the dossier folder or export a support bundle.";
        }
        catch
        {
            HasCrashBanner = false;
        }
    }

    private void OpenCrashFolder()
    {
        var path = _activeDossier?.DirectoryPath ?? _writer?.CrashRoot;
        if (string.IsNullOrWhiteSpace(path))
        {
            CrashStatus = "No crash dossier available.";
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            OpenFolder(path);
            CrashStatus = $"Opened: {path}";
        }
        catch (Exception ex)
        {
            CrashStatus = $"Open failed: {ex.Message}";
        }
    }

    private void ExportSupportBundle()
    {
        if (_activeDossier is null || _settingsStore is null)
        {
            CrashStatus = "No crash dossier to export.";
            return;
        }

        try
        {
            var exports = Path.Combine(_settingsStore.RootDirectory, "exports");
            Directory.CreateDirectory(exports);
            var zipName = $"crash-{_activeDossier.DossierId}.zip";
            var dest = Path.Combine(exports, zipName);
            var written = CrashDossierWriter.TryExportZip(_activeDossier.DirectoryPath, dest);
            CrashStatus = written is null
                ? "Export failed."
                : $"Exported support bundle: {written}";
        }
        catch (Exception ex)
        {
            CrashStatus = $"Export failed: {ex.Message}";
        }
    }

    private void DismissCrashBanner()
    {
        if (_activeDossier is not null)
        {
            CrashDossierWriter.TryMarkReviewed(_activeDossier.DirectoryPath);
        }

        _activeDossier = null;
        HasCrashBanner = false;
        CrashBannerTitle = string.Empty;
        CrashBannerDetail = string.Empty;
        CrashStatus = "Dismissed. Dossier kept on disk.";
        RefreshCrashBanner();
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
