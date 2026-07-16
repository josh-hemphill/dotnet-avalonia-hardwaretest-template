using HardwareTest.Core.Settings;
using HardwareTest.Features;
using HardwareTest.Features.Home;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void NavigateToPageId_updates_ui_state()
    {
        var store = new FakeSettingsStore();
        store.UiState.SelectedPageId = "Home";

        var settings = new AppSettings();
        var openTap = new FakeOpenTapSession();
        var runControl = new FakeRunControl();
        var runTest = new RunTestViewModel(
            openTap,
            new OperatorSession(),
            runControl,
            new FakeReportService(),
            new FakeRunStore(),
            settings);
        var results = new ResultsViewModel(new FakeRunStore(), new FakeReportService());
        var preview = new ReportPreviewViewModel(new FakeRunStore(), new FakeReportService());
        var instruments = new InstrumentsViewModel(store, new FakeVisaDiscovery(), openTap);
        var settingsVm = new SettingsViewModel(store);
        var vm = new MainWindowViewModel(
            store,
            new HomeViewModel(),
            runTest,
            results,
            preview,
            instruments,
            settingsVm,
            runControl,
            openTap);

        vm.NavigateToPageId("Results");

        Assert.Equal("Results", store.UiState.SelectedPageId);
        Assert.Same(results, vm.CurrentPage);
        Assert.Equal("Results", vm.SelectedItem?.Id);
    }
}
