using System.ComponentModel;
using HardwareTest.Reporting.NativePrinting;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class PdfViewerLauncherTests
{
    [Theory]
    [InlineData(31)]
    [InlineData(1155)]
    public async Task Missing_association_opens_owned_chooser_for_exact_file(int error)
    {
        var backend = new Backend { OpenError = new Win32Exception(error) };
        var worker = new Worker();
        var launcher = new PdfViewerLauncher(_ => Task.FromResult((nint)42), backend, worker);
        await launcher.OpenAsync("older-revision.pdf");
        Assert.Equal("older-revision.pdf", backend.ChosenPath);
        Assert.Equal((nint)42, backend.Owner);
        Assert.Equal(1, worker.Calls);
#pragma warning disable CA1416
        Assert.Equal(0x24u, WindowsPdfViewerBackend.OpenWithFlags);
#pragma warning restore CA1416
    }

    [Fact]
    public async Task Unrelated_shell_error_does_not_open_chooser()
    {
        var backend = new Backend { OpenError = new Win32Exception(5) };
        var launcher = new PdfViewerLauncher(_ => Task.FromResult((nint)42), backend, new Worker());
        await Assert.ThrowsAsync<Win32Exception>(() => launcher.OpenAsync("report.pdf"));
        Assert.Null(backend.ChosenPath);
    }

    [Fact]
    public async Task Chooser_cancellation_propagates_without_claiming_opened()
    {
        var backend = new Backend { OpenError = new Win32Exception(1155), ChooseError = new OperationCanceledException() };
        var launcher = new PdfViewerLauncher(_ => Task.FromResult((nint)42), backend, new Worker());
        await Assert.ThrowsAsync<OperationCanceledException>(() => launcher.OpenAsync("report.pdf"));
    }

    private sealed class Backend : IPdfViewerBackend
    {
        public Exception? OpenError { get; init; }
        public Exception? ChooseError { get; init; }
        public string? ChosenPath { get; private set; }
        public nint Owner { get; private set; }
        public void Open(string pdfPath) { if (OpenError is not null) throw OpenError; }
        public void ChooseApplication(nint owner, string pdfPath)
        {
            Owner = owner;
            ChosenPath = pdfPath;
            if (ChooseError is not null) throw ChooseError;
        }
    }

    private sealed class Worker : IPrintWorker
    {
        public int Calls { get; private set; }
        public Task<T> RunAsync<T>(Func<T> action) { Calls++; return Task.Run(action); }
    }
}
