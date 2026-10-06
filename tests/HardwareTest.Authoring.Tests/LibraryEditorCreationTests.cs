using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class LibraryEditorCreationTests
{
    [Fact]
    public void Ordinary_editor_requires_physical_address_and_only_demo_gets_mock_default()
    {
        var package = Environment.GetEnvironmentVariable("HARDWARETEST_LIBRARY_TEST_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(package)) Assert.Skip("Actual upstream package required for editor integration.");
        var root = Path.Combine(Path.GetTempPath(), "ht-library-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            new AuthoringWorkspaceInitializer().Create(new(root, "Library editor", "Library editor") { IncludeLibraryPackage = true });
            var vm = new AuthoringWorkspaceViewModel(); vm.Open(root);
            var home = new OpenTapHome(Path.Combine(root, "actual-home"));
            vm.Workspace!.Manifest.InstrumentComponentsPackage = package;
            new OpenTapHomeBootstrapper().Bootstrap(vm.Workspace, new() { HomeDirectory = home.Root, Offline = true });
            vm.OpenTapHomeOverride = home.Root; vm.CreateProgram("editor");
            vm.NewInstrumentTypeId = AuthoringInstrumentCatalog.Discover(home).Single(adapter => adapter.DisplayName == "DC Power Supply").TypeId;
            vm.NewInstrumentSlot = "Rail";
            var before = vm.SelectedProgram;
            foreach (var blank in new[] { "", "   " })
            {
                vm.NewInstrumentVisa = blank;
                Assert.False(vm.CanAddInstrumentSlot);
                Assert.Contains("address", vm.NewInstrumentAvailabilityText);
                Assert.Throws<AuthoringWorkspaceException>(vm.AddInstrumentSlot);
                Assert.Same(before, vm.SelectedProgram);
                Assert.Equal("Rail", vm.NewInstrumentSlot); Assert.Equal(blank, vm.NewInstrumentVisa);
                Assert.DoesNotContain("Rail", vm.Workspace.Manifest.Catalogs?.InstrumentSlotNames ?? []);
            }
            vm.NewInstrumentVisa = "TCPIP0::192.0.2.8::inst0::INSTR";
            var type = vm.NewInstrumentTypeId;
            Assert.True(vm.CanAddInstrumentSlot); vm.AddInstrumentSlot();
            var physical = Assert.Single(vm.SelectedProgram!.Instruments);
            Assert.Equal(type, physical.TypeId); Assert.Equal("TCPIP0::192.0.2.8::inst0::INSTR", physical.VisaAddress);
            vm.NewInstrumentTypeId = typeof(MockDmmInstrument).FullName!;
            vm.NewInstrumentSlot = "Demo"; vm.NewInstrumentVisa = "";
            Assert.True(vm.CanAddInstrumentSlot); vm.AddInstrumentSlot();
            Assert.Equal("MOCK::INSTR1", vm.SelectedProgram.Instruments.Single(resource => resource.SlotName == "Demo").VisaAddress);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
