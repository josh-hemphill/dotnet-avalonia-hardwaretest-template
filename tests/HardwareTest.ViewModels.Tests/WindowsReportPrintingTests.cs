using System.Runtime.InteropServices;
using HardwareTest.Reporting.NativePrinting;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class WindowsReportPrintingTests
{
    [Fact]
    public async Task Native_worker_runs_on_dedicated_STA_and_survives_failed_job()
    {
        if (!OperatingSystem.IsWindows()) return;
        IPrintWorker worker = new StaPrintWorker();
        var caller = Environment.CurrentManagedThreadId;
        var observed = await worker.RunAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        Assert.NotEqual(caller, observed.Item1);
        Assert.Equal(ApartmentState.STA, observed.Item2);
        await Assert.ThrowsAsync<IOException>(() => worker.RunAsync<int>(() => throw new IOException("renderer failure")));
        Assert.Equal(42, await worker.RunAsync(() => 42));
    }

    [Fact]
    public void Native_dialog_layout_and_flags_match_Windows_ABI()
    {
#pragma warning disable CA1416
        Assert.Equal(IntPtr.Size == 8 ? 136 : 84, Marshal.SizeOf<WindowsPrintBackend.NativePrintDialog>());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<WindowsPrintBackend.NativePrintDialog>("Owner").ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 132 : 80, Marshal.OffsetOf<WindowsPrintBackend.NativePrintDialog>("ResultAction").ToInt32());
        Assert.Equal(0x00000100u, WindowsPrintBackend.DialogFlags & 0x00000100u);
        Assert.Equal(0x00040000u, WindowsPrintBackend.DialogFlags & 0x00040000u);
        Assert.Equal(0x00180000u, WindowsPrintBackend.DialogFlags & 0x00180000u);
#pragma warning restore CA1416
    }

    [Fact]
    public async Task Prints_all_pages_beyond_preview_limit_once_with_driver_copies_and_no_viewer()
    {
        var fixture = new Fixture(15);
        fixture.Session.DriverCopies = 3;
        var result = await fixture.Service.PrintJobAsync("report.pdf");
        Assert.Equal(PrintOutcome.Submitted, result.Outcome);
        Assert.Equal(Enumerable.Range(0, 15), fixture.Document.Rendered);
        Assert.Equal(15, fixture.Session.Drawn);
        Assert.Equal(1, fixture.Session.DocumentsStarted);
        Assert.Equal(1, fixture.Session.DocumentsEnded);
        Assert.Equal(0, fixture.Session.Aborted);
        Assert.True(fixture.Document.Disposed);
        Assert.True(fixture.Session.Disposed);
        Assert.All(fixture.Document.Pages, page => Assert.True(page.Disposed));
        Assert.Equal((nint)42, fixture.Backend.Owner);
    }

    [Fact]
    public async Task Requested_ranges_are_sorted_deduplicated_and_include_later_pages()
    {
        var fixture = new Fixture(25);
        fixture.Session.UsesPageRanges = true;
        fixture.Session.PageRanges = [new(20, 25), new(1, 3), new(2, 4)];
        Assert.Equal(PrintOutcome.Submitted, (await fixture.Service.PrintJobAsync("report.pdf")).Outcome);
        Assert.Equal(new[] { 0, 1, 2, 3, 19, 20, 21, 22, 23, 24 }, fixture.Document.Rendered);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 26)]
    [InlineData(4, 3)]
    public async Task Invalid_ranges_fail_before_spooling(int from, int to)
    {
        var fixture = new Fixture(25);
        fixture.Session.UsesPageRanges = true;
        fixture.Session.PageRanges = [new(from, to)];
        Assert.Equal(PrintOutcome.Failed, (await fixture.Service.PrintJobAsync("report.pdf")).Outcome);
        Assert.Equal(0, fixture.Session.DocumentsStarted);
        Assert.True(fixture.Session.Disposed);
    }

    [Fact]
    public void Fit_preserves_physical_aspect_with_nonsquare_dpi_and_printable_margins()
    {
        var paper = new PdfPageSize(612, 792);
        var printer = new PrinterMetrics(4800, 3150, 600, 300, 150, 75);
        var fit = PrintGeometry.Fit(paper, printer);
        var physicalRatio = fit.Width / (double)printer.DpiX / (fit.Height / (double)printer.DpiY);
        Assert.InRange(Math.Abs(physicalRatio - 612d / 792), 0, 0.001);
        Assert.True(fit.X >= 0 && fit.Y >= 0);
        Assert.True(fit.X + fit.Width <= printer.WidthPixels);
        Assert.True(fit.Y + fit.Height <= printer.HeightPixels);
        Assert.True(fit.X + printer.PhysicalOffsetX >= printer.PhysicalOffsetX);
    }

    [Fact]
    public async Task Oversized_page_is_rejected_before_raster_allocation_or_StartDoc()
    {
        var fixture = new Fixture(1);
        fixture.Document.PageSizes = [new(72000, 72000)];
        Assert.Equal(PrintOutcome.Failed, (await fixture.Service.PrintJobAsync("huge.pdf")).Outcome);
        Assert.Empty(fixture.Document.Rendered);
        Assert.Equal(0, fixture.Session.DocumentsStarted);
        Assert.True(fixture.Session.Disposed);
    }

    [Theory]
    [InlineData(PrintDialogAction.Cancel, PrintOutcome.Cancelled)]
    [InlineData(PrintDialogAction.Apply, PrintOutcome.Applied)]
    public async Task Cancel_and_Apply_never_submit_document(PrintDialogAction action, PrintOutcome outcome)
    {
        var fixture = new Fixture(2);
        fixture.Session.Action = action;
        Assert.Equal(outcome, (await fixture.Service.PrintJobAsync("report.pdf")).Outcome);
        Assert.Equal(0, fixture.Session.DocumentsStarted);
        Assert.True(fixture.Session.Disposed);
        Assert.True(fixture.Document.Disposed);
    }

    [Theory]
    [InlineData("render")]
    [InlineData("draw")]
    [InlineData("end")]
    public async Task Cancellation_between_native_stages_aborts_and_releases_resources(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(2);
        if (stage == "render") fixture.Document.OnRender = cancellation.Cancel;
        if (stage == "draw") fixture.Session.OnDraw = cancellation.Cancel;
        if (stage == "end") fixture.Session.OnEndPage = cancellation.Cancel;
        var result = await fixture.Service.PrintJobAsync("report.pdf", cancellation.Token);
        Assert.Equal(PrintOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, fixture.Session.Aborted);
        Assert.Equal(0, fixture.Session.DocumentsEnded);
        Assert.True(fixture.Session.Disposed);
        Assert.True(fixture.Document.Disposed);
        Assert.All(fixture.Document.Pages, page => Assert.True(page.Disposed));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("draw")]
    [InlineData("end")]
    public async Task Printer_failures_are_reported_without_viewer_fallback(string stage)
    {
        var fixture = new Fixture(1);
        fixture.Session.FailureStage = stage;
        var result = await fixture.Service.PrintJobAsync("report.pdf");
        Assert.Equal(PrintOutcome.Failed, result.Outcome);
        Assert.Contains("Print failed", result.Message);
        Assert.Equal(stage == "start" ? 0 : 1, fixture.Session.Aborted);
        Assert.True(fixture.Session.Disposed);
        Assert.All(fixture.Document.Pages, page => Assert.True(page.Disposed));
    }

    [Fact]
    public async Task Cancellation_during_blocking_driver_call_keeps_job_gate_until_call_returns()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        fixture.Session.OnDraw = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); };
        var active = fixture.Service.PrintJobAsync("first.pdf", cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.False(active.IsCompleted);
        Assert.False(fixture.Session.Disposed);
        var competing = await fixture.Service.PrintJobAsync("second.pdf");
        Assert.Equal(PrintOutcome.Failed, competing.Outcome);
        Assert.Contains("already in progress", competing.Message);
        release.Set();
        Assert.Equal(PrintOutcome.Cancelled, (await active).Outcome);
        Assert.True(fixture.Session.Disposed);
        Assert.Equal(1, fixture.Session.Aborted);
    }

    [Fact]
    public async Task Cancellation_after_native_dialog_returns_disposes_without_starting_document()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(1);
        fixture.Backend.OnDialog = cancellation.Cancel;
        var result = await fixture.Service.PrintJobAsync("report.pdf", cancellation.Token);
        Assert.Equal(PrintOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, fixture.Session.DocumentsStarted);
        Assert.True(fixture.Session.Disposed);
        Assert.True(fixture.Document.Disposed);
    }

    [Fact]
    public async Task Native_dialog_failure_disposes_pdf_and_releases_job_gate()
    {
        var fixture = new Fixture(1);
        fixture.Backend.OnDialog = () => throw new IOException("PrintDlgEx failed");
        Assert.Equal(PrintOutcome.Failed, (await fixture.Service.PrintJobAsync("first.pdf")).Outcome);
        Assert.True(fixture.Document.Disposed);
        fixture.Backend.OnDialog = null;
        Assert.Equal(PrintOutcome.Submitted, (await fixture.Service.PrintJobAsync("retry.pdf")).Outcome);
    }

    [Fact]
    public async Task Successful_EndDoc_confirms_submission_even_if_cancellation_arrives_inside_driver_call()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(1);
        fixture.Session.OnEndDocument = cancellation.Cancel;
        var result = await fixture.Service.PrintJobAsync("report.pdf", cancellation.Token);
        Assert.Equal(PrintOutcome.Submitted, result.Outcome);
        Assert.Equal(1, fixture.Session.DocumentsEnded);
        Assert.Equal(0, fixture.Session.Aborted);
        Assert.True(fixture.Session.Disposed);
    }

    private sealed class Fixture
    {
        public Document Document { get; }
        public Session Session { get; } = new();
        public Backend Backend { get; }
        public WindowsReportPrintService Service { get; }
        public Fixture(int pageCount)
        {
            Document = new Document(pageCount);
            Backend = new Backend(Session);
            Service = new WindowsReportPrintService(_ => Task.FromResult((nint)42), Backend, new Renderer(Document), new Worker());
        }
    }
    private sealed class Worker : IPrintWorker { public Task<T> RunAsync<T>(Func<T> action) => Task.Run(action); }
    private sealed class Renderer(Document document) : IPdfPrintRenderer { public IPdfPrintDocument Open(string pdfPath) => document; }
    private sealed class Backend(Session session) : IWindowsPrintBackend
    {
        public nint Owner { get; private set; }
        public Action? OnDialog { get; set; }
        public IWindowsPrintSession ShowDialog(nint owner, int pageCount) { Owner = owner; OnDialog?.Invoke(); return session; }
    }
    private sealed class Document(int pageCount) : IPdfPrintDocument
    {
        public IReadOnlyList<PdfPageSize> PageSizes { get; set; } = Enumerable.Repeat(new PdfPageSize(612, 792), pageCount).ToArray();
        public List<int> Rendered { get; } = [];
        public List<Page> Pages { get; } = [];
        public Action? OnRender { get; set; }
        public bool Disposed { get; private set; }
        public IPrintPage RenderPage(int zeroBasedPage)
        {
            Rendered.Add(zeroBasedPage);
            var page = new Page();
            Pages.Add(page);
            OnRender?.Invoke();
            return page;
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class Page : IPrintPage
    {
        public int Width => 1;
        public int Height => 1;
        public int Stride => 4;
        public nint Pixels => 1;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Session : IWindowsPrintSession
    {
        public PrintDialogAction Action { get; set; } = PrintDialogAction.Print;
        public bool UsesPageRanges { get; set; }
        public IReadOnlyList<PrintPageRange> PageRanges { get; set; } = [];
        public PrinterMetrics Metrics => new(4800, 6300, 600, 600);
        public int DriverCopies { get; set; } = 1;
        public int DocumentsStarted { get; private set; }
        public int DocumentsEnded { get; private set; }
        public int Drawn { get; private set; }
        public int Aborted { get; private set; }
        public bool Disposed { get; private set; }
        public string? FailureStage { get; set; }
        public Action? OnDraw { get; set; }
        public Action? OnEndPage { get; set; }
        public Action? OnEndDocument { get; set; }
        public void StartDocument(string title) { if (FailureStage == "start") throw new IOException("StartDoc failed"); DocumentsStarted++; }
        public void StartPage() { }
        public void DrawPage(IPrintPage page, PrintRectangle rectangle) { if (FailureStage == "draw") throw new IOException("StretchDIBits failed"); Drawn++; OnDraw?.Invoke(); }
        public void EndPage() => OnEndPage?.Invoke();
        public void EndDocument() { if (FailureStage == "end") throw new IOException("EndDoc failed"); DocumentsEnded++; OnEndDocument?.Invoke(); }
        public void AbortDocument() => Aborted++;
        public void Dispose() => Disposed = true;
    }
}
