using HardwareTest.Core.Hardware;
using Xunit;

namespace HardwareTest.Tests.Hardware;

public sealed class VisaDiscoveryTests
{
    [Fact]
    public async Task Mock_discovery_returns_catalog()
    {
        var discovery = new MockVisaResourceDiscovery();
        var found = await discovery.FindAsync();
        Assert.True(found.Count >= 3);
        Assert.Contains(found, r => r.Resource == "MOCK::INSTR0");
    }

    [Fact]
    public async Task Configurable_mock_discovery_preserves_resource_metadata()
    {
        var discovery = new ConfigurableVisaResourceDiscovery(useMockVisa: true);

        var found = await discovery.FindAsync();

        Assert.Equal(MockVisaResourceDiscovery.Catalog, found);
        Assert.All(found, resource =>
        {
            Assert.Equal("MOCK", resource.Interface);
            Assert.True(resource.SupportsMessageQuery);
            Assert.False(resource.LooksLikeAlias);
        });
    }

    [Fact]
    public async Task Mock_discovery_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MockVisaResourceDiscovery().FindAsync(cancellation.Token));
    }
}
