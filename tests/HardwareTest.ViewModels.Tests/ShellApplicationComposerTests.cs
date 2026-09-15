using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellApplicationComposerTests
{
    [Fact]
    public void BindPages_registers_guest_page_and_view_factory()
    {
        var views = new ViewRegistry();
        var vm = new object();
        var app = new StubShellApplication(
            id: "vendor.planning",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.planning",
            viewModelType: vm.GetType(),
            viewModel: vm,
            placement: ShellPagePlacement.Engineer);

        var pages = ShellApplicationComposer.BindPages([app], new NullServices(), views);

        Assert.Single(pages);
        Assert.Equal("vendor.planning", pages[0].Descriptor.Id);
        Assert.Same(vm, pages[0].ViewModel);
        Assert.True(views.IsRegistered(vm.GetType()));
    }

    [Fact]
    public void BindPages_rejects_reserved_page_id()
    {
        var views = new ViewRegistry();
        var app = new StubShellApplication(
            id: "vendor.bad",
            minHostAbi: ShellHostAbi.Current,
            pageId: ShellBuiltInPageIds.Home,
            viewModelType: typeof(object),
            viewModel: new object(),
            placement: ShellPagePlacement.Operator);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationComposer.BindPages([app], new NullServices(), views));
        Assert.Contains(ShellBuiltInPageIds.Home, ex.Message, StringComparison.Ordinal);
        Assert.False(views.IsRegistered(typeof(object)));
    }

    [Fact]
    public void BindPages_rejects_newer_host_abi()
    {
        var views = new ViewRegistry();
        var app = new StubShellApplication(
            id: "vendor.future",
            minHostAbi: ShellHostAbi.Current + 1,
            pageId: "vendor.future",
            viewModelType: typeof(object),
            viewModel: new object(),
            placement: ShellPagePlacement.Engineer);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationComposer.BindPages([app], new NullServices(), views));
        Assert.Contains("ABI", ex.Message, StringComparison.Ordinal);
    }

    private sealed class NullServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            _ = serviceType;
            return null;
        }
    }

    private sealed class StubShellApplication : IShellApplication
    {
        public StubShellApplication(
            string id,
            int minHostAbi,
            string pageId,
            Type viewModelType,
            object viewModel,
            ShellPagePlacement placement)
        {
            Id = id;
            MinHostAbi = minHostAbi;
            Pages =
            [
                new ShellPageRegistration
                {
                    Descriptor = new ShellPageDescriptor
                    {
                        Id = pageId,
                        Title = pageId,
                        SymbolName = "Document",
                        ViewModelType = viewModelType,
                        Placement = placement,
                        Order = 60,
                    },
                    CreateViewModel = _ => viewModel,
                    CreateView = static () => new object(),
                },
            ];
        }

        public string Id { get; }
        public string Title => Id;
        public string Version => "0.0.0";
        public int MinHostAbi { get; }
        public void Configure(IServiceCollection services)
            => _ = services;

        public IReadOnlyList<ShellPageRegistration> Pages { get; }
    }
}
