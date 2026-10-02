using HardwareTest.Reporting.NativePrinting;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class LpReportPrintServiceTests
{
    [Fact]
    public void Exact_path_is_one_argument_without_shell_interpretation()
    {
        const string path = "/reports/old revision;$(touch bad).pdf";
        var start = SystemLpPrintBackend.CreateStartInfo(path);
        Assert.Equal("lp", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal([path], start.ArgumentList);
        Assert.Empty(start.Arguments);
    }

    [Theory]
    [InlineData(0, "Submitted to printer")]
    [InlineData(1, "Print failed")]
    public async Task Reports_submission_only_after_successful_exit(int code, string expected)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend(completion.Task);
        var operation = new LpReportPrintService(backend).PrintAsync("revision.pdf");
        Assert.False(operation.IsCompleted);
        completion.SetResult(code);
        Assert.StartsWith(expected, await operation);
        Assert.Equal("revision.pdf", backend.Path);
    }

    [Fact]
    public async Task Missing_lp_does_not_claim_submission_or_fall_back_to_viewer()
    {
        var service = new LpReportPrintService(new Backend(Task.FromException<int>(new IOException("lp is unavailable"))));
        Assert.StartsWith("Print failed", await service.PrintAsync("revision.pdf"));
    }

    private sealed class Backend(Task<int> result) : ILpPrintBackend
    {
        public string? Path { get; private set; }
        public Task<int> SubmitAsync(string pdfPath, CancellationToken cancellationToken) { Path = pdfPath; return result; }
    }
}
