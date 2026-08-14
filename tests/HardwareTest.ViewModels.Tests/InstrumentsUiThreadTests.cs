using HardwareTest.Core.Hardware;
using HardwareTest.Features.Instruments;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

internal sealed class YieldingVisaDiscovery : IVisaResourceDiscovery
{
    public async Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return MockVisaResourceDiscovery.Catalog;
    }
}

public sealed class InstrumentsUiThreadTests
{
    [Fact]
    public async Task RefreshVisaDiscover_sets_IsBusy_only_via_UiScheduler()
    {
        var vm = new InstrumentsViewModel(
            new FakeSettingsStore(),
            new YieldingVisaDiscovery(),
            new FakeOpenTapSession(),
            new MockVisaSessionFactory(new VisaSessionGate()));
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

        await vm.RefreshVisaDiscoverCommand.ExecuteAsync();

        Assert.False(vm.IsBusy);
        Assert.True(vm.DiscoveredVisa.Count > 0, vm.Status);
        Assert.Equal(0, offScheduler);
    }
}
