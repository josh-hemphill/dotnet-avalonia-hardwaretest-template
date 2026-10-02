using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HardwareTest.Core.IO;
using HardwareTest.Reporting.NativePrinting;

namespace HardwareTest.Reporting;

public interface IReportPrintService
{
    Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default);
}

public interface IReportDesktopActions
{
    Task<string?> SaveCopyAsync(string pdfPath, CancellationToken cancellationToken = default);
    Task OpenInViewerAsync(string pdfPath, CancellationToken cancellationToken = default);
}

public sealed class ReportDesktopActions(Func<Window?> window, string managedRunsDirectory, PdfViewerLauncher? viewer = null) : IReportDesktopActions
{
    public async Task<string?> SaveCopyAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var destination = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = window() ?? throw new InvalidOperationException("No desktop window is available.");
            return await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save a copy",
                SuggestedFileName = Path.GetFileName(pdfPath),
                DefaultExtension = "pdf",
                FileTypeChoices = [new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] }],
                ShowOverwritePrompt = true,
            });
        });
        if (destination is null) return null;
        using (destination)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(destination.TryGetLocalPath(), Path.GetFullPath(pdfPath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a different file to preserve this report.");
            var localPath = destination.TryGetLocalPath()
                ?? throw new IOException("Choose a local folder to save a report copy safely.");
            await Task.Run(() => CopyToLocalPathAsync(pdfPath, localPath, cancellationToken, managedRunsDirectory), cancellationToken);
            return destination.Name;
        }
    }

    public static async Task CopyToLocalPathAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default,
        string? managedRunsDirectory = null)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different file to preserve this report.");
        if (managedRunsDirectory is not null && PathContainment.IsUnderRoot(ResolveDirectory(managedRunsDirectory),
                Path.Combine(ResolveDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!), Path.GetFileName(destinationPath))))
            throw new IOException("Save copies outside the managed run folder to preserve report history.");
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await AtomicFile.WriteAllBytesAsync(destinationPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                var directory = new DirectoryInfo(current);
                if (directory.LinkTarget is not null) current = directory.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
            }
        }
        return current;
    }

    public Task OpenInViewerAsync(string pdfPath, CancellationToken cancellationToken = default) => viewer?.OpenAsync(pdfPath, cancellationToken) ?? Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
    }, cancellationToken);
}
