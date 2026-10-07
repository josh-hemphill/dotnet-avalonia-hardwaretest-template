using System.Diagnostics;

namespace HardwareTest.Reporting.NativePrinting;

public interface ILpPrintBackend
{
    Task<int> SubmitAsync(string pdfPath, CancellationToken cancellationToken);
}

public sealed class LpReportPrintService(ILpPrintBackend backend) : IReportPrintService
{
    public async Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exitCode = await backend.SubmitAsync(pdfPath, cancellationToken).ConfigureAwait(false);
            return exitCode == 0
                ? "Submitted to printer."
                : $"Print failed: lp exited with code {exitCode}. Retry, save a copy, or open the PDF viewer.";
        }
        catch (OperationCanceledException) { return "Print cancelled. No submission was confirmed."; }
        catch (Exception ex) { return "Print failed: " + ex.Message + " Retry, save a copy, or open the PDF viewer."; }
    }
}

public sealed class SystemLpPrintBackend : ILpPrintBackend
{
    internal static ProcessStartInfo CreateStartInfo(string pdfPath)
    {
        var start = new ProcessStartInfo("lp") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(pdfPath);
        return start;
    }

    public Task<int> SubmitAsync(string pdfPath, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(CreateStartInfo(pdfPath)) ?? throw new IOException("lp could not be started.");
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            // Reap the process before releasing its resources. A spooler may already have accepted the job.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return process.ExitCode;
    }, cancellationToken);
}
