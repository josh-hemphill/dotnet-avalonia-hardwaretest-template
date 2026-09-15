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
    public void BindPages_rejects_duplicate_guest_page_id()
    {
        var views = new ViewRegistry();
        var first = new StubShellApplication(
            id: "vendor.a",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.shared",
            viewModelType: typeof(StubShellApplication),
            viewModel: new object(),
            placement: ShellPagePlacement.Engineer);
        var second = new StubShellApplication(
            id: "vendor.b",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.shared",
            viewModelType: typeof(ShellApplicationComposerTests),
            viewModel: new object(),
            placement: ShellPagePlacement.Engineer);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationComposer.BindPages([first, second], new NullServices(), views));
        Assert.Contains("vendor.shared", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindPages_rejects_viewmodel_type_already_used_by_another_guest()
    {
        var views = new ViewRegistry();
        var sharedType = typeof(ShellApplicationComposerTests);
        var first = new StubShellApplication(
            id: "vendor.a",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.a",
            viewModelType: sharedType,
            viewModel: this,
            placement: ShellPagePlacement.Engineer);
        var second = new StubShellApplication(
            id: "vendor.b",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.b",
            viewModelType: sharedType,
            viewModel: this,
            placement: ShellPagePlacement.Engineer);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationComposer.BindPages([first, second], new NullServices(), views));
        Assert.Contains("ViewModel", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindPages_allows_reregistering_the_same_guest_type_on_reload()
    {
        var views = new ViewRegistry();
        var app = new StubShellApplication(
            id: "vendor.reload",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.reload",
            viewModelType: typeof(ShellApplicationComposerTests),
            viewModel: this,
            placement: ShellPagePlacement.Engineer);

        var first = ShellApplicationComposer.BindPages([app], new NullServices(), views);
        var second = ShellApplicationComposer.BindPages([app], new NullServices(), views);
        Assert.Equal("vendor.reload", Assert.Single(first).Descriptor.Id);
        Assert.Equal("vendor.reload", Assert.Single(second).Descriptor.Id);
        Assert.True(views.IsRegistered(typeof(ShellApplicationComposerTests)));
    }

    [Fact]
    public void BindPages_rejects_registered_viewmodel_type()
    {
        var views = new ViewRegistry();
        var app = new StubShellApplication(
            id: "vendor.homevm",
            minHostAbi: ShellHostAbi.Current,
            pageId: "vendor.homevm",
            viewModelType: typeof(HardwareTest.Features.Home.HomeViewModel),
            viewModel: new object(),
            placement: ShellPagePlacement.Engineer);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationComposer.BindPages([app], new NullServices(), views));
        Assert.Contains("ViewModel", ex.Message, StringComparison.Ordinal);
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
