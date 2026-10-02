using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HardwareTest.Reporting.NativePrinting;

public interface IPdfViewerBackend
{
    void Open(string pdfPath);
    void ChooseApplication(nint owner, string pdfPath);
}

public sealed class PdfViewerLauncher(
    Func<CancellationToken, Task<nint>> ownerHandle,
    IPdfViewerBackend backend,
    IPrintWorker worker)
{
    public async Task OpenAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); backend.Open(pdfPath); }, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 31 or 1155)
        {
            var owner = await ownerHandle(cancellationToken).ConfigureAwait(false);
            if (owner == 0) throw new InvalidOperationException("No window is available for the PDF viewer chooser.");
            await worker.RunAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                backend.ChooseApplication(owner, pdfPath);
                return true;
            }).ConfigureAwait(false);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPdfViewerBackend : IPdfViewerBackend
{
    internal const uint OpenWithFlags = 0x00000004 | 0x00000020; // EXEC | HIDE_REGISTRATION: no default-association change.
    public void Open(string pdfPath)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
    }
    public void ChooseApplication(nint owner, string pdfPath)
    {
        var options = new OpenAsInfo { File = pdfPath, Flags = OpenWithFlags };
        var result = SHOpenWithDialog(owner, ref options);
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80004004))
            throw new OperationCanceledException("PDF viewer selection cancelled.");
        Marshal.ThrowExceptionForHR(result);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public uint Flags;
    }
    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(nint owner, ref OpenAsInfo info);
}
