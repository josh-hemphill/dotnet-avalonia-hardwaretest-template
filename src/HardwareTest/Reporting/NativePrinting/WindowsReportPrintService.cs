using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HardwareTest.Reporting.NativePrinting;

public sealed class WindowsReportPrintService(
    Func<CancellationToken, Task<nint>> ownerHandle,
    IWindowsPrintBackend backend,
    IPdfPrintRenderer renderer,
    IPrintWorker worker) : IReportPrintService
{
    private readonly SemaphoreSlim _jobGate = new(1, 1);

    public async Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default)
        => (await PrintJobAsync(pdfPath, cancellationToken).ConfigureAwait(false)).Message;

    public async Task<PrintResult> PrintJobAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        var acquired = false;
        try
        {
            acquired = await _jobGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
            if (!acquired) return new(PrintOutcome.Failed, "A print operation is already in progress. Wait for it to finish.");
            var owner = await ownerHandle(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (owner == 0) throw new InvalidOperationException("No window is available to own the native print dialog.");
            // Do not cancel this await: a blocking native call still owns the document/DC until it returns.
            return await worker.RunAsync(() => Execute(pdfPath, owner, cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(PrintOutcome.Cancelled, "Print cancelled. No submission was confirmed."); }
        catch (Exception ex) { return new(PrintOutcome.Failed, "Print failed: " + ex.Message + " Retry, save a copy, or open the PDF viewer."); }
        finally { if (acquired) _jobGate.Release(); }
    }

    private PrintResult Execute(string path, nint owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var document = renderer.Open(path);
        token.ThrowIfCancellationRequested();
        using var session = backend.ShowDialog(owner, document.PageSizes.Count);
        token.ThrowIfCancellationRequested();
        if (session.Action == PrintDialogAction.Cancel) return new(PrintOutcome.Cancelled, "Print cancelled.");
        if (session.Action == PrintDialogAction.Apply) return new(PrintOutcome.Applied, "Printer settings applied. No document was submitted.");
        var ranges = PrintGeometry.NormalizeRanges(document.PageSizes.Count, session.UsesPageRanges, session.PageRanges);
        foreach (var page in PrintGeometry.EnumeratePages(ranges)) PrintGeometry.ValidateRasterSize(document.PageSizes[page]);
        var started = false;
        try
        {
            token.ThrowIfCancellationRequested();
            session.StartDocument(Path.GetFileName(path));
            started = true;
            token.ThrowIfCancellationRequested();
            foreach (var index in PrintGeometry.EnumeratePages(ranges))
            {
                token.ThrowIfCancellationRequested();
                using var page = document.RenderPage(index);
                token.ThrowIfCancellationRequested();
                var destination = PrintGeometry.Fit(document.PageSizes[index], session.Metrics);
                session.StartPage();
                token.ThrowIfCancellationRequested();
                session.DrawPage(page, destination);
                token.ThrowIfCancellationRequested();
                session.EndPage();
                token.ThrowIfCancellationRequested();
            }
            token.ThrowIfCancellationRequested();
            session.EndDocument();
            started = false;
            return new(PrintOutcome.Submitted, "Submitted to printer.");
        }
        finally
        {
            if (started)
            {
                try { session.AbortDocument(); } catch { /* Cleanup must still release native handles. */ }
            }
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class StaPrintWorker : IPrintWorker
{
    public Task<T> RunAsync<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Marshal.ThrowExceptionForHR(OleInitialize(0));
                T value;
                try { value = action(); }
                finally { OleUninitialize(); }
                result.TrySetResult(value);
            }
            catch (Exception ex) { result.TrySetException(ex); }
        })
        { IsBackground = true, Name = "Report native print" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
}
