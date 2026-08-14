using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Features.ReportPreview;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ReportPreviewUiThreadTests
{
    [Fact]
    public async Task LoadLatest_sets_Status_only_via_UiScheduler()
    {
        var store = new YieldingRunStore();
        var vm = new ReportPreviewViewModel(store, new FakeReportService());
        var inScheduler = false;
        var offScheduler = 0;
        vm.UiScheduler = action =>
        {
            inScheduler = true;
            try
            {
                action();
            }
            finally
            {
                inScheduler = false;
            }
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Status) && !inScheduler)
            {
                Interlocked.Increment(ref offScheduler);
            }
        };

        await vm.LoadLatestCommand.ExecuteAsync();

        Assert.Equal("No saved runs.", vm.Status);
        Assert.Equal(0, offScheduler);
    }

    [Fact]
    public async Task LoadFromPath_sets_IsBusy_only_via_UiScheduler()
    {
        var vm = new ReportPreviewViewModel(new YieldingRunStore(), new FakeReportService());
        var inScheduler = false;
        var offScheduler = 0;
        vm.UiScheduler = action =>
        {
            inScheduler = true;
            try
            {
                action();
            }
            finally
            {
                inScheduler = false;
            }
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsBusy) && !inScheduler)
            {
                Interlocked.Increment(ref offScheduler);
            }
        };

        await vm.LoadFromPathAsync(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".pdf"));

        Assert.False(vm.IsBusy);
        Assert.Contains("File not found", vm.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, offScheduler);
    }
}
