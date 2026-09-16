using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HardwareTest.Core.Crash;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Storage;
using HardwareTest.Crash;
using HardwareTest.Features.Shell;
using HardwareTest.Shell;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Home;

public partial class HomeViewModel : ReactiveObject
{
    private readonly ISettingsStore? _settingsStore;
    private readonly CrashDossierWriter? _writer;
    private readonly IExportTargetService? _exportTargets;
    private readonly ShellNotificationViewModel? _shellNotification;

    private static readonly ShellHomeTile[] BuiltInTiles =
    [
        new()
        {
            Title = "Getting started",
            Body = "Open Run, confirm the DUT serial, pick the sample OpenTAP program, and start. Identity check and voltage sweep run under OpenTAP with mock instruments by default.",
            ActionLabel = "Open Run →",
            NavigatePageId = ShellBuiltInPageIds.RunTest,
            Placement = ShellPagePlacement.Operator,
        },
        new()
        {
            Title = "Programs & instruments",
            Body = "Locked OpenTAP .TapPlan programs ship under Programs/. Use Instruments to discover VISA resources and bind station roles for the bench.",
            ActionLabel = "Open Instruments →",
            NavigatePageId = ShellBuiltInPageIds.Instruments,
            Placement = ShellPagePlacement.Engineer,
        },
        new()
        {
            Title = "Reports",
            Body = "Passed and failed runs write Typst PDFs. Open a run from Results, then preview or export the report from there.",
            ActionLabel = "Open Results →",
            NavigatePageId = ShellBuiltInPageIds.Results,
            Placement = ShellPagePlacement.Operator,
        },
    ];

    public HomeViewModel()
        : this(null)
    {
    }

    public HomeViewModel(
        ISettingsStore? settingsStore,
        IExportTargetService? exportTargets = null,
        ShellNotificationViewModel? shellNotification = null,
        IEnumerable<ShellHomeTile>? guestTiles = null)
    {
        _settingsStore = settingsStore;
        _exportTargets = exportTargets;
        _shellNotification = shellNotification;
        if (settingsStore is not null)
        {
            _writer = CrashDossierWriter.FromSettings(settingsStore.AppSettings, settingsStore.RootDirectory);
        }

        AllowOsFolderBrowse = settingsStore?.AppSettings.AllowOsFolderBrowse == true
                              || settingsStore?.AppSettings.IsEngineerDebugMode == true;
        IsEngineerMode = settingsStore?.AppSettings.IsEngineerDebugMode == true;
        HomeShellTileViewModel CreateTile(ShellHomeTile tile)
            => new(tile, IsEngineerMode, pageId => NavigateToPageRequested?.Invoke(this, pageId));
        var builtIn = BuiltInTiles.Select(CreateTile).ToArray();
        GuestTiles = (guestTiles ?? []).Select(CreateTile).ToArray();
        Tiles = [.. builtIn, .. GuestTiles];
        if (settingsStore is not null)
        {
            settingsStore.AppSettingsSaved += (_, _) => ApplyEngineerPresentation(
                settingsStore.AppSettings.IsEngineerDebugMode);
        }
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

    public IReadOnlyList<HomeShellTileViewModel> Tiles { get; }

    public IReadOnlyList<HomeShellTileViewModel> GuestTiles { get; }

    /// Raised with the target page ID when a CTA button is pressed.
    public event EventHandler<string>? NavigateToPageRequested;

    /// Updates Home engineer chrome and tile visibility to match presentation.
    public void ApplyEngineerPresentation(bool engineerMode)
    {
        IsEngineerMode = engineerMode;
        if (_settingsStore is not null)
        {
            AllowOsFolderBrowse = _settingsStore.AppSettings.AllowOsFolderBrowse || engineerMode;
        }

        foreach (var tile in Tiles)
        {
            tile.RefreshVisibility(engineerMode);
        }
    }

    [Reactive] private bool _hasCrashBanner;
    [Reactive] private string _crashBannerTitle = string.Empty;
    [Reactive] private string _crashBannerDetail = string.Empty;
    [Reactive] private string _crashStatus = string.Empty;
    [Reactive] private bool _allowOsFolderBrowse;
    [Reactive] private bool _isEngineerMode;

    private CrashDossierSummary? _activeDossier;

    /// Host state for an unreviewed crash (presented by the shell strip — not an in-page card).
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
                    PublishCrashToShell(isFatal: false);
                    return;
                }

                HasCrashBanner = false;
                CrashBannerTitle = string.Empty;
                CrashBannerDetail = string.Empty;
                ClearCrashFromShell();
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
            PublishCrashToShell(isFatal: _activeDossier.IsFatal);
        }
        catch (Exception ex)
        {
            HasCrashBanner = false;
            CrashBannerTitle = string.Empty;
            CrashBannerDetail = string.Empty;
            CrashStatus = $"Could not load crash dossier: {ex.Message}";
            ClearCrashFromShell();
        }
    }

    private void PublishCrashToShell(bool isFatal)
    {
        if (_shellNotification is null)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(CrashBannerDetail)
            ? CrashBannerTitle
            : $"{CrashBannerTitle}. {CrashBannerDetail}";
        ShellNotificationAction? secondary = AllowOsFolderBrowse
            ? new ShellNotificationAction { Label = "Open folder", Command = OpenCrashFolderCommand }
            : null;
        _shellNotification.Publish(
            isFatal ? ShellNotificationSeverity.Critical : ShellNotificationSeverity.Error,
            message,
            dismissible: true,
            sourceKey: ShellNotificationViewModel.SourceCrash,
            primary: new ShellNotificationAction
            {
                Label = "Export support bundle",
                Command = ExportSupportBundleCommand,
            },
            secondary: secondary,
            onDismissed: MarkCrashReviewedQuiet);
    }

    private void ClearCrashFromShell()
        => _shellNotification?.Clear(ShellNotificationViewModel.SourceCrash);

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
        MarkCrashReviewedQuiet();
        ClearCrashFromShell();
        CrashStatus = "Dismissed. Dossier kept on disk.";
        RefreshCrashBanner();
    }

    /// Marks the active dossier reviewed without republishing the shell strip.
    private void MarkCrashReviewedQuiet()
    {
        if (_activeDossier is not null)
        {
            CrashDossierWriter.TryMarkReviewed(_activeDossier.DirectoryPath);
        }

        _activeDossier = null;
        HasCrashBanner = false;
        CrashBannerTitle = string.Empty;
        CrashBannerDetail = string.Empty;
    }

    private static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
