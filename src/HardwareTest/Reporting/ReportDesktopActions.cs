using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HardwareTest.Core.IO;

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

/// Baseline OS printing route; Windows native printing replaces this implementation separately.
public sealed class SystemReportPrintService : IReportPrintService
{
    public Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true, Verb = "print" });
            return "Sent PDF to the system print handler.";
        }
        var start = new ProcessStartInfo("lp") { UseShellExecute = false };
        start.ArgumentList.Add(pdfPath);
        using var queued = Process.Start(start);
        return "Submitted PDF to lp.";
    }, cancellationToken);
}

public sealed class ReportDesktopActions(Func<Window?> window) : IReportDesktopActions
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
            await Task.Run(async () =>
            {
                var localPath = destination.TryGetLocalPath();
                if (localPath is not null)
                {
                    await CopyToLocalPathAsync(pdfPath, localPath, cancellationToken);
                    return;
                }
                var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
                await using var output = await destination.OpenWriteAsync();
                if (output.CanSeek) output.SetLength(0);
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }, cancellationToken);
            return destination.Name;
        }
    }

    public static async Task CopyToLocalPathAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different file to preserve this report.");
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await AtomicFile.WriteAllBytesAsync(destinationPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    public Task OpenInViewerAsync(string pdfPath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
    }, cancellationToken);
}
