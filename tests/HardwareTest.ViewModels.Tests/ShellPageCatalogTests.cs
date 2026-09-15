using HardwareTest.Shell;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellPageCatalogTests
{
    [Fact]
    public void Constructor_rejects_duplicate_ids()
    {
        var descriptor = new ShellPageDescriptor
        {
            Id = "dup",
            Title = "Dup",
            SymbolName = "Home",
            ViewModelType = typeof(object),
            Placement = ShellPagePlacement.Operator,
            Order = 0,
        };
        var first = new ShellPage { Descriptor = descriptor, ViewModel = new object() };
        var second = new ShellPage { Descriptor = descriptor, ViewModel = new object() };
        var ex = Assert.Throws<InvalidOperationException>(() => new ShellPageCatalog([first, second]));
        Assert.Contains("dup", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_returns_page_by_id()
    {
        var page = new ShellPage
        {
            Descriptor = new ShellPageDescriptor
            {
                Id = "notes",
                Title = "Notes",
                SymbolName = "Document",
                ViewModelType = typeof(object),
                Placement = ShellPagePlacement.Engineer,
                Order = 60,
            },
            ViewModel = new object(),
        };
        var catalog = new ShellPageCatalog([page]);
        Assert.Same(page, catalog.Find("notes"));
        Assert.Null(catalog.Find("missing"));
    }
}
