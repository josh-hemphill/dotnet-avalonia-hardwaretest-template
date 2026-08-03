using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Crash;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Home;

public partial class HomeViewModel : ReactiveObject
{
    private readonly ISettingsStore? _settingsStore;
    private readonly CrashDossierWriter? _writer;
    private readonly IExportTargetService? _exportTargets;

    public HomeViewModel()
        : this(null)
    {
    }

    public HomeViewModel(ISettingsStore? settingsStore, IExportTargetService? exportTargets = null)
    {
        _settingsStore = settingsStore;
        _exportTargets = exportTargets;
        if (settingsStore is not null)
        {
            _writer = CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory);
        }

        AllowOsFolderBrowse = settingsStore?.AppSettings.AllowOsFolderBrowse == true
                              || settingsStore?.AppSettings.IsEngineerDebugMode == true;
        OpenCrashFolderCommand = ReactiveCommand.Create(OpenCrashFolder);
        ExportSupportBundleCommand = ReactiveCommand.Create(ExportSupportBundle);
        DismissCrashBannerCommand = ReactiveCommand.Create(DismissCrashBanner);
        NavigateToRunCommand = ReactiveCommand.Create(
            () => NavigateToPageRequested?.Invoke(this, "RunTest"));
        NavigateToInstrumentsCommand = ReactiveCommand.Create(
            () => NavigateToPageRequested?.Invoke(this, "Instruments"));
        NavigateToResultsCommand = ReactiveCommand.Create(
            () => NavigateToPageRequested?.Invoke(this, "Results"));
        RefreshCrashBanner();
        CrashHandler.RecoverableCrashOccurred += (_, _) => RefreshCrashBanner();
    }

    public string Title { get; } = "Hardware Test";

    public string Summary { get; } =
        "Confirm a DUT once, run locked OpenTAP programs from Avalonia, manage station instruments, and publish Typst reports with live plots when needed.";

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenCrashFolderCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ExportSupportBundleCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> DismissCrashBannerCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToRunCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToInstrumentsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavigateToResultsCommand { get; }

    /// Raised with the target page ID when a CTA button is pressed.
    public event EventHandler<string>? NavigateToPageRequested;

    [Reactive] private bool _hasCrashBanner;
    [Reactive] private string _crashBannerTitle = string.Empty;
    [Reactive] private string _crashBannerDetail = string.Empty;
    [Reactive] private string _crashStatus = string.Empty;
    [Reactive] private bool _allowOsFolderBrowse;

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
                    CrashStatus = string.Empty;
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
            CrashBannerDetail = $"{when} — {fault} — app {ver}. Export a support bundle" +
                                (AllowOsFolderBrowse ? " or open the dossier folder." : ".");
            CrashStatus = string.Empty;
        }
        catch (Exception ex)
        {
            HasCrashBanner = false;
            CrashBannerTitle = string.Empty;
            CrashBannerDetail = string.Empty;
            CrashStatus = $"Could not load crash dossier: {ex.Message}";
        }
    }

    private void OpenCrashFolder()
    {
        if (!AllowOsFolderBrowse)
        {
            CrashStatus = "Open folder is disabled on this appliance. Export a support bundle instead.";
            return;
        }

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
            var zipName = $"crash-{_activeDossier.DossierId}.zip";
            var target = _exportTargets?.ListTargets().FirstOrDefault();
            string dest;
            if (target is not null && _exportTargets is not null)
            {
                // Stage zip then atomic-copy via package write of bytes.
                var tempDir = Path.Combine(Path.GetTempPath(), "hwtest-export-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    var staged = Path.Combine(tempDir, zipName);
                    var written = CrashDossierWriter.TryExportZip(_activeDossier.DirectoryPath, staged);
                    if (written is null || !File.Exists(written))
                    {
                        CrashStatus = "Export failed.";
                        return;
                    }

                    dest = _exportTargets.WriteAtomic(target, zipName, File.ReadAllBytes(written));
                }
                finally
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
            else
            {
                var exports = Path.Combine(_settingsStore.RootDirectory, "exports");
                Directory.CreateDirectory(exports);
                dest = Path.Combine(exports, zipName);
                var written = CrashDossierWriter.TryExportZip(_activeDossier.DirectoryPath, dest);
                if (written is null)
                {
                    CrashStatus = "Export failed.";
                    return;
                }

                dest = written;
            }

            CrashStatus = $"Exported support bundle: {dest}";
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
