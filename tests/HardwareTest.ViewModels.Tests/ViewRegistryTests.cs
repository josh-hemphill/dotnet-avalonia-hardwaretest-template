using HardwareTest.Features.Home;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ViewRegistryTests
{
    [Fact]
    public void Built_in_home_viewmodel_is_registered()
    {
        var registry = new ViewRegistry();
        Assert.True(registry.IsRegistered(typeof(HomeViewModel)));
        Assert.True(registry.Match(new HomeViewModel()));
        Assert.False(registry.Match(null));
    }

    [Fact]
    public void Register_adds_a_mapping_without_type_scanning()
    {
        var registry = new ViewRegistry();
        Assert.False(registry.IsRegistered(typeof(ViewRegistryTests)));
        Assert.False(registry.Match(this));
        registry.Register(typeof(ViewRegistryTests), static () => new Avalonia.Controls.Control());
        Assert.True(registry.IsRegistered(typeof(ViewRegistryTests)));
        Assert.True(registry.Match(this));
    }
}
