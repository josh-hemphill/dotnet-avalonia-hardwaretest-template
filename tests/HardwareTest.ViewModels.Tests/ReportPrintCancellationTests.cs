using HardwareTest.Core.Runs;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Reporting;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ReportPrintCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "print-ui-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Cancel_keeps_print_active_until_backend_returns_then_allows_retry()
    {
        var (vm, printer, _) = await SetupAsync();
        var print = vm.PrintCommand.ExecuteAsync();
        await printer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsPrinting);
        await vm.CancelPrintCommand.ExecuteAsync();
        Assert.True(printer.Token.IsCancellationRequested);
        Assert.True(vm.IsPrinting);
        Assert.False(print.IsCompleted);
        Assert.Contains("Waiting for the printer driver", vm.PrintStatus);
        printer.Release.SetResult("Print cancelled. No submission was confirmed.");
        await print;
        Assert.False(vm.IsPrinting);
        Assert.StartsWith("Print cancelled", vm.PrintStatus);
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(2, printer.Paths.Count);
        Assert.Equal("Submitted to printer.", vm.Status);
    }

    [Fact]
    public async Task Navigation_cancels_captured_print_without_retargeting_or_overwriting_new_selection_status()
    {
        var (vm, printer, otherPath) = await SetupAsync();
        var firstPath = vm.PdfPath;
        var print = vm.PrintCommand.ExecuteAsync();
        await printer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.PrintFromPathAsync(otherPath);
        Assert.True(printer.Token.IsCancellationRequested);
        Assert.True(vm.IsPrinting);
        Assert.Equal(firstPath, Assert.Single(printer.Paths));
        var currentStatus = vm.Status;
        printer.Release.SetResult("Print cancelled. No submission was confirmed.");
        await print;
        Assert.False(vm.IsPrinting);
        Assert.Equal(otherPath, vm.PdfPath);
        Assert.Equal(currentStatus, vm.Status);
        Assert.StartsWith("Previous print job:", vm.PrintStatus);
        await vm.PrintCommand.ExecuteAsync();
        Assert.Equal(otherPath, printer.Paths[1]);
    }

    [Fact]
    public async Task Print_state_and_status_changes_run_through_UI_scheduler()
    {
        var (vm, printer, _) = await SetupAsync();
        var inScheduler = false;
        var violations = 0;
        vm.UiScheduler = action => { inScheduler = true; try { action(); } finally { inScheduler = false; } };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(vm.IsPrinting) or nameof(vm.PrintStatus) && !inScheduler)
                Interlocked.Increment(ref violations);
        };
        var print = vm.PrintCommand.ExecuteAsync();
        await printer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.CancelPrintCommand.ExecuteAsync();
        printer.Release.SetResult("Print cancelled.");
        await print;
        Assert.Equal(0, violations);
    }

    private async Task<(ReportPreviewViewModel, Printer, string)> SetupAsync()
    {
        var store = new FileRunStore(_root);
        var first = await SeedAsync(store, "first");
        var other = await SeedAsync(store, "other");
        var printer = new Printer();
        var vm = new ReportPreviewViewModel(store, new FakeReportService(), printer: printer)
        { UiScheduler = action => action(), PreviewRenderer = _ => [] };
        await vm.LoadFromPathAsync(first);
        return (vm, printer, other);
    }

    private static async Task<string> SeedAsync(FileRunStore store, string id)
    {
        var path = Path.Combine(store.GetRunDirectory(id), "status.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4 test seam");
        await store.SaveAsync(new TestRunRecord { RunId = id, Reports = [new() { Kind = "status", PdfPath = path }] });
        return path;
    }

    private sealed class Printer : IReportPrintService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public List<string> Paths { get; } = [];
        public async Task<string> PrintAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            Paths.Add(pdfPath);
            if (Paths.Count > 1) return "Submitted to printer.";
            Token = cancellationToken;
            Started.TrySetResult();
            // Model a native call that retains its resources despite cancellation.
            return await Release.Task;
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
