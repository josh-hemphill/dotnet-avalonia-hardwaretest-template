using Avalonia.Headless.XUnit;
using HardwareTest.Features.Instruments;
using Xunit;

namespace HardwareTest.E2E.Tests;

public sealed class InstrumentsE2ETests
{
    [AvaloniaFact]
    public async Task Instruments_page_discovers_mock_resources()
    {
        var window = E2EHarness.ShowMainWindow();
        var main = E2EHarness.MainVm(window);
        main.NavigateToPageId("Instruments");
        var instruments = (InstrumentsViewModel)main.CurrentPage!;
        await instruments.RefreshDiscoverCommand.ExecuteAsync();
        Assert.True(instruments.Discovered.Count >= 1, instruments.Status);
        var item = instruments.Discovered[0];
        Assert.False(string.IsNullOrWhiteSpace(item.Title));
        Assert.Equal(item.Resource, item.Subtitle);
    }
}
