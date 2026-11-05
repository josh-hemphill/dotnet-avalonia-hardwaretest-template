using System.IO;
using System.Threading;
using HardwareTest.OpenTap.Host;
using HardwareTest.UiThreading;
using ReactiveUI;

namespace HardwareTest.Features.Settings;

internal sealed record OfflineBundleInspection(string Summary, string Details);

public partial class SettingsViewModel
{
    private string _offlineBundlePath = string.Empty;
    private string _offlineBundleDestination = string.Empty;
    private string _offlineBundleDataDirectory = string.Empty;
    private string _offlineBundleStatus = "Choose a copied bundle folder to inspect its contents.";
    private string _offlineBundleSummary = string.Empty;
    private string _offlineBundleDetails = string.Empty;
    private string _offlineBundleCommandStatus = string.Empty;
    private bool _offlineBundleIsChecking;
    private long _offlineBundleGeneration;
    private CancellationTokenSource? _offlineBundleCheck;
    private FileSystemWatcher? _offlineBundleWatcher;

    internal Func<string, CancellationToken, Task<OfflineBundleInspection>> OfflineBundleInspector { get; set; }
        = InspectOfflineBundleAsync;
    internal long OfflineBundleGeneration => Interlocked.Read(ref _offlineBundleGeneration);
    public event Action<string>? OfflineBundleFocusRequested;

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CheckOfflineBundleCommand { get; private set; } = null!;
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CancelOfflineBundleCommand { get; private set; } = null!;
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CopyOfflineBundleCommand { get; private set; } = null!;
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CopyOfflineBundleDetailsCommand { get; private set; } = null!;

    public string OfflineBundlePath
    {
        get => _offlineBundlePath;
        set
        {
            value ??= string.Empty;
            if (value == _offlineBundlePath) return;
            InvalidateOfflineBundle("Bundle selection changed. Check this folder before using its installation command.");
            this.RaiseAndSetIfChanged(ref _offlineBundlePath, value);
        }
    }

    public string OfflineBundleDestination
    {
        get => _offlineBundleDestination;
        set
        {
            value ??= string.Empty;
            if (value == _offlineBundleDestination) return;
            this.RaiseAndSetIfChanged(ref _offlineBundleDestination, value);
            OfflineBundleCommandStatus = string.Empty;
            this.RaisePropertyChanged(nameof(OfflineBundleInstallationCommand));
        }
    }

    public string OfflineBundleDataDirectory
    {
        get => _offlineBundleDataDirectory;
        set
        {
            value ??= string.Empty;
            if (value == _offlineBundleDataDirectory) return;
            this.RaiseAndSetIfChanged(ref _offlineBundleDataDirectory, value);
            OfflineBundleCommandStatus = string.Empty;
            this.RaisePropertyChanged(nameof(OfflineBundleInstallationCommand));
        }
    }

    public bool OfflineBundleIsChecking
    {
        get => _offlineBundleIsChecking;
        private set
        {
            this.RaiseAndSetIfChanged(ref _offlineBundleIsChecking, value);
            this.RaisePropertyChanged(nameof(OfflineBundleCanEdit));
        }
    }
    public bool OfflineBundleCanEdit => !OfflineBundleIsChecking;
    public bool HasOfflineBundleInspection => _offlineBundleDetails.Length > 0;
    public string OfflineBundleStatus { get => _offlineBundleStatus; private set => this.RaiseAndSetIfChanged(ref _offlineBundleStatus, value); }
    public string OfflineBundleSummary => _offlineBundleSummary;
    public string OfflineBundleDetails => _offlineBundleDetails;
    public string OfflineBundleCommandStatus { get => _offlineBundleCommandStatus; private set => this.RaiseAndSetIfChanged(ref _offlineBundleCommandStatus, value); }
    public string OfflineBundleInstallationCommand => !HasOfflineBundleInspection || string.IsNullOrWhiteSpace(OfflineBundleDestination) || string.IsNullOrWhiteSpace(OfflineBundleDataDirectory)
        ? string.Empty : BuildOfflineBundleInstallationCommand(OfflineBundlePath, OfflineBundleDestination, OfflineBundleDataDirectory);
    public string OfflineBundleNextSteps => "1. Use the matching operator build and target listed above.\n"
        + "2. Copy the installation command and run it outside this app. It rechecks the bundle, creates a separate operator copy and binds it to a FRESH data folder. Both folders must be new.\n"
        + "3. Stop the old app. Launch the new copy using its bound fresh data folder and explicitly reviewed station launch options. Existing data-directory reuse is unsupported; credentials and station data are not copied.\n"
        + "4. Reload programs, verify the program and instrument bindings, then qualify the station before hardware execution.";

    private void InitOfflineBundles()
    {
        CheckOfflineBundleCommand = ReactiveCommand.CreateFromTask(CheckOfflineBundleAsync);
        CancelOfflineBundleCommand = ReactiveCommand.Create(() => InvalidateOfflineBundle("Bundle check cancelled. The path is retained; check it again when ready."));
        CopyOfflineBundleCommand = ReactiveCommand.CreateFromTask(CopyOfflineBundleInstallationCommandAsync);
        CopyOfflineBundleDetailsCommand = ReactiveCommand.CreateFromTask(() => CopyOfflineBundleTextAsync(OfflineBundleDetails, "Copied bundle inspection details."));
    }

    private static bool IsOfflineBundleProperty(string? name) => name?.StartsWith("OfflineBundle", StringComparison.Ordinal) == true
        || name == nameof(HasOfflineBundleInspection);

    internal async Task CheckOfflineBundleAsync()
    {
        if (OfflineBundleIsChecking) return;
        InvalidateOfflineBundle(string.Empty);
        string root;
        try { root = RequireAbsoluteFolder(OfflineBundlePath, "bundle"); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            OfflineBundleStatus = error.Message;
            OfflineBundleFocusRequested?.Invoke("Offline bundle folder");
            return;
        }
        var generation = OfflineBundleGeneration;
        var cancellation = new CancellationTokenSource();
        _offlineBundleCheck = cancellation;
        OfflineBundleIsChecking = true;
        OfflineBundleStatus = "Checking bundle files, package pins and target. No installation is being performed.";
        try
        {
            WatchOfflineBundle(root, generation);
            var inspection = await OfflineBundleInspector(root, cancellation.Token).ConfigureAwait(false);
            await UiDispatch.RunAsync(() =>
            {
                if (generation != OfflineBundleGeneration || cancellation.IsCancellationRequested) return;
                _offlineBundleSummary = inspection.Summary;
                _offlineBundleDetails = $"Inspection snapshot at {DateTimeOffset.UtcNow:O}\nFolder: {root}\n\n{inspection.Details}\n\n{OfflineBundleNextSteps}";
                OfflineBundleStatus = "Bundle inspection passed. Nothing has been installed; installation rechecks these files.";
                RaiseOfflineBundleInspection();
            }, UiScheduler).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (generation != OfflineBundleGeneration) return;
                DisposeOfflineBundleWatcher();
                OfflineBundleStatus = $"Cannot verify this bundle: {error.Message} Keep the path, correct or recopy the complete matching bundle, then check again.";
                OfflineBundleFocusRequested?.Invoke("Offline bundle folder");
            }, UiScheduler).ConfigureAwait(false);
        }
        finally
        {
            await UiDispatch.RunAsync(() =>
            {
                if (ReferenceEquals(_offlineBundleCheck, cancellation)) _offlineBundleCheck = null;
                if (generation == OfflineBundleGeneration) OfflineBundleIsChecking = false;
            }, UiScheduler).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    public void StopOfflineBundleInspection() => InvalidateOfflineBundle("Bundle inspection was closed. Recheck this folder before installation.");

    internal void OfflineBundleFolderPickerUnavailable()
    {
        OfflineBundleStatus = "The folder chooser is unavailable. Type or paste the copied bundle's absolute folder path, then choose Check bundle.";
        OfflineBundleFocusRequested?.Invoke("Offline bundle folder");
    }

    private void InvalidateOfflineBundle(string message)
    {
        Interlocked.Increment(ref _offlineBundleGeneration);
        var check = _offlineBundleCheck;
        _offlineBundleCheck = null;
        check?.Cancel();
        DisposeOfflineBundleWatcher();
        OfflineBundleIsChecking = false;
        _offlineBundleSummary = string.Empty;
        _offlineBundleDetails = string.Empty;
        OfflineBundleStatus = message;
        OfflineBundleCommandStatus = string.Empty;
        RaiseOfflineBundleInspection();
    }

    private void WatchOfflineBundle(string root, long generation)
    {
        if (!Directory.Exists(root)) return; // The validator supplies the actionable missing-folder error.
        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        void Changed() => UiDispatch.Post(() =>
        {
            if (generation == OfflineBundleGeneration && ReferenceEquals(_offlineBundleWatcher, watcher))
                InvalidateOfflineBundle("Bundle files changed or could not be watched. Recheck the complete folder before installation.");
        }, UiScheduler);
        watcher.Changed += (_, _) => Changed();
        watcher.Created += (_, _) => Changed();
        watcher.Deleted += (_, _) => Changed();
        watcher.Renamed += (_, _) => Changed();
        watcher.Error += (_, _) => Changed();
        _offlineBundleWatcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    private void DisposeOfflineBundleWatcher()
    {
        var watcher = _offlineBundleWatcher;
        _offlineBundleWatcher = null;
        watcher?.Dispose();
    }

    private void RaiseOfflineBundleInspection()
    {
        this.RaisePropertyChanged(nameof(HasOfflineBundleInspection));
        this.RaisePropertyChanged(nameof(OfflineBundleSummary));
        this.RaisePropertyChanged(nameof(OfflineBundleDetails));
        this.RaisePropertyChanged(nameof(OfflineBundleInstallationCommand));
    }

    private async Task CopyOfflineBundleInstallationCommandAsync()
    {
        if (!HasOfflineBundleInspection || OfflineBundleIsChecking)
        { OfflineBundleCommandStatus = "Check the selected bundle before copying an installation command."; return; }
        try
        {
            var destination = RequireAbsoluteFolder(OfflineBundleDestination, "new installation");
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new ArgumentException("The installation folder already exists. Choose a NEW folder; the installer preserves existing contents.");
            var data = RequireAbsoluteFolder(OfflineBundleDataDirectory, "fresh data");
            if (Directory.Exists(data) || File.Exists(data))
                throw new ArgumentException("The data folder already exists. Choose a FRESH folder; existing station data cannot be reused.");
            if (PathsOverlap(destination, data))
                throw new ArgumentException("Use separate NEW installation and FRESH data folders.");
            var bundle = RequireAbsoluteFolder(OfflineBundlePath, "bundle");
            if (PathsOverlap(bundle, destination))
                throw new ArgumentException("Choose a new installation folder outside the bundle folder.");
            if (PathsOverlap(bundle, data))
                throw new ArgumentException("Choose a fresh data folder outside the bundle folder.");
            var command = BuildOfflineBundleInstallationCommand(bundle, destination, data);
            await CopyOfflineBundleTextAsync(command, "Copied installation command. Run it outside this app; nothing has been installed.").ConfigureAwait(false);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            OfflineBundleCommandStatus = error.Message;
            OfflineBundleFocusRequested?.Invoke(error.Message.Contains("data", StringComparison.OrdinalIgnoreCase) ? "Fresh operator data folder" : "New operator copy folder");
        }
    }

    private async Task CopyOfflineBundleTextAsync(string text, string success)
    {
        if (!HasOfflineBundleInspection || OfflineBundleIsChecking) return;
        var generation = OfflineBundleGeneration;
        try
        {
            if (CopyTextAsync is null) throw new InvalidOperationException("Clipboard is unavailable. The inspection details remain selectable.");
            await CopyTextAsync(text).ConfigureAwait(false);
            await UiDispatch.RunAsync(() => { if (generation == OfflineBundleGeneration) OfflineBundleCommandStatus = success; }, UiScheduler).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await UiDispatch.RunAsync(() => { if (generation == OfflineBundleGeneration) OfflineBundleCommandStatus = $"Copy failed: {error.Message}"; }, UiScheduler).ConfigureAwait(false);
        }
    }

    private static string RequireAbsoluteFolder(string path, string purpose)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"Enter an absolute {purpose} folder path.");
        if (path.Contains('\r') || path.Contains('\n') || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"Use one absolute {purpose} folder path without line breaks.");
        return Path.GetFullPath(path.Trim());
    }

    private static bool PathsOverlap(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        first = Path.TrimEndingDirectorySeparator(first); second = Path.TrimEndingDirectorySeparator(second);
        var firstPrefix = Path.EndsInDirectorySeparator(first) ? first : first + Path.DirectorySeparatorChar;
        var secondPrefix = Path.EndsInDirectorySeparator(second) ? second : second + Path.DirectorySeparatorChar;
        return string.Equals(first, second, comparison) || first.StartsWith(secondPrefix, comparison)
            || second.StartsWith(firstPrefix, comparison);
    }

    internal static string BuildOfflineBundleInstallationCommand(string bundle, string destination, string dataDirectory, bool? windows = null, string? operatorEntry = null)
    {
        var useWindows = windows ?? OperatingSystem.IsWindows();
        Func<string, string> quote = useWindows ? QuotePowerShell : QuotePosix;
        var entry = operatorEntry ?? typeof(HardwareTest.App).Assembly.Location;
        if (string.IsNullOrEmpty(entry)) entry = Path.Combine(AppContext.BaseDirectory, "HardwareTest.dll");
        var appHost = Path.Combine(AppContext.BaseDirectory, "HardwareTest" + (useWindows ? ".exe" : string.Empty));
        var launcher = operatorEntry is null && File.Exists(appHost) ? quote(appHost) : "dotnet " + quote(entry);
        return (useWindows ? "& " : string.Empty) + launcher + " --install-offline-bundle " + quote(bundle)
            + " --destination " + quote(destination) + " --data-directory " + quote(dataDirectory);
    }
    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static string QuotePosix(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static Task<OfflineBundleInspection> InspectOfflineBundleAsync(string root, CancellationToken cancellation)
        => Task.Run(() =>
        {
            var manifest = OfflineBenchRuntime.CheckBundle(root, AppContext.BaseDirectory,
                OfflineBenchBundle.CurrentOS, OfflineBenchBundle.CurrentArchitecture, cancellation);
            var pins = string.Join("; ", manifest.Packages.Take(3).Select(package => $"{package.Name} {package.Version}"))
                + (manifest.Packages.Count > 3 ? $"; {manifest.Packages.Count - 3} more in full details" : string.Empty);
            return new OfflineBundleInspection($"Verified program {manifest.PlanId} · {manifest.TargetOS}/{manifest.TargetArchitecture}\n"
                + $"{manifest.Packages.Count} pinned package(s): {pins}", OfflineBenchBundle.Describe(manifest));
        }, cancellation);
}
