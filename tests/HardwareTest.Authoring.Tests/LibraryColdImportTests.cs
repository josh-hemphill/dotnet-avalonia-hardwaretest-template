using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
[Trait("Category", "AuthoringIntegration")]
public sealed class LibraryColdImportTests
{
    [Fact]
    public async Task Actual_mixed_resource_import_preserves_unknown_configuration_and_cold_library_lifecycle_source()
    {
        var package = PublishedLibraryFixture.PackageRoot;
        var root = Path.Combine(Path.GetTempPath(), "ht-library-cold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new OpenTapHome(Path.Combine(root, "actual-home"));
            var workspace = new AuthoringWorkspace(root, new AuthoringManifest
            {
                Dependencies = [new() { Package = AuthoringInstrumentCatalog.LibraryPackage, Version = "^0.1.0" }],
                InstrumentComponentsPackage = package
            }, []);
            new OpenTapHomeBootstrapper().Bootstrap(workspace, new() { HomeDirectory = home.Root, Offline = true });
            var supply = AuthoringInstrumentCatalog.Discover(home).Single(adapter => adapter.DisplayName == "DC Power Supply");
            var binding = new InstrumentRef("Supply", supply.TypeId, "TCPIP0::192.0.2.8::inst0::INSTR")
            { Settings = new Dictionary<string, string> { ["IoTimeoutMilliseconds"] = "8123" } };
            var vendor = new InstrumentRef("Vendor", typeof(MockDmmInstrument).FullName!, "MOCK::VENDOR");
            var draft = new AuthoringPlanInitializer().Construct(new("mixed")
            { Home = home, Instruments = [binding, vendor], IdentityInstrumentSlot = "Supply" }).Draft;
            draft = draft with { Setup = [.. draft.Setup, new IdentitySetup("Vendor")] };
            var path = Path.Combine(root, "mixed.TapPlan");
            new PlanCompiler(selectedHome: home).Save(draft, path);
            var xml = XDocument.Load(path);
            foreach (var resource in xml.Descendants().Where(element => element.Name.LocalName == "Instrument"
                && element.Attribute("type") is not null && element.Elements().Any(child => child.Name.LocalName == "Name" && child.Value == "Vendor")))
            {
                resource.SetAttributeValue("type", "Vendor.UnknownInstrument");
                resource.Add(new XElement("VendorCalibration", "preserved-calibration"));
            }
            xml.Save(path);
            var original = File.ReadAllBytes(path);
            var expected = xml.Descendants().Where(element => element.Name.LocalName == "Instrument" && element.Attribute("type") is not null)
                .GroupBy(element => element.Elements().Single(child => child.Name.LocalName == "Name").Value)
                .ToDictionary(group => group.Key, group => group.First().ToString(SaveOptions.DisableFormatting));
            var missing = new OpenTapHome(Path.Combine(root, "missing-home"));
            var compiler = new PlanCompiler(selectedHome: missing);
            var imported = compiler.Load(path);
            AssertResources(imported, expected, binding);
            Assert.Contains(imported.Setup.OfType<IdentitySetup>(), setup => setup.InstrumentSlot == "Vendor");
            Assert.Equal(["Supply", "Vendor"], imported.Cleanup.InstrumentSlots);
            var store = new AuthoringDocumentStore(root); store.Save(AuthoringDocumentDto.FromDraft(imported));
            var sourcePath = store.GetDocumentPath("mixed"); var sourceBytes = File.ReadAllBytes(sourcePath);
            Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(imported, path));
            Assert.Equal(original, File.ReadAllBytes(path)); Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));

            var coldRoot = Path.Combine(root, "cold"); Directory.CreateDirectory(coldRoot);
            var coldPath = Path.Combine(coldRoot, "mixed.authoring.json");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll"));
            start.ArgumentList.Add("--cold-library-import"); start.ArgumentList.Add(path); start.ArgumentList.Add(coldPath);
            using var child = Process.Start(start)!;
            try
            {
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(child.ExitCode == 0, await error);
                Assert.Contains("cold-library-source-preserved", await output);
            }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
            var cold = JsonSerializer.Deserialize(File.ReadAllBytes(coldPath), AuthoringDocumentJsonContext.Default.AuthoringDocumentDto)!.ToDraft();
            AssertResources(cold, expected, binding);
            var librarySteps = xml.Descendants().Where(element => element.Name.LocalName == "TestStep"
                && ((string?)element.Attribute("type"))?.Contains("InstrumentComponents.OpenTap.", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(2, librarySteps.Length);
            foreach (var step in librarySteps)
            {
                var raw = Assert.Single(cold.Measure.OfType<RawStepNode>(), node => node.NodeId == Guid.Parse((string)step.Attribute("Id")!));
                Assert.Equal(((string)step.Attribute("type")!).Replace("emb:", "", StringComparison.Ordinal), raw.TypeName);
                Assert.Equal(step.ToString(SaveOptions.DisableFormatting), raw.XmlFragment);
                Assert.DoesNotContain("TestGroupStep", raw.TypeName);
            }
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Contains(AuthoringIssueService.GetIssues(cold, home), issue => issue.Code == "LIBRARY_LIFECYCLE_REIMPORT");
            // Even once the real package is available, opaque lifecycle rows must not move into Measure.
            var phaseError = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler(selectedHome: home).Save(cold with { Instruments = [binding] }, path));
            Assert.Contains("LIBRARY_LIFECYCLE_REIMPORT", phaseError.Message);
            Assert.Equal(original, File.ReadAllBytes(path));
            var recovered = new PlanCompiler(selectedHome: home).Load(path);
            Assert.Equal(draft.Setup.OfType<IdentitySetup>().Single(setup => setup.InstrumentSlot == "Supply").NodeId,
                recovered.Setup.OfType<IdentitySetup>().Single(setup => setup.InstrumentSlot == "Supply").NodeId);
            Assert.Equal(draft.Cleanup.NodeId, recovered.Cleanup.NodeId);
            Assert.Equal(["Supply", "Vendor"], recovered.Cleanup.InstrumentSlots);
            Assert.DoesNotContain(recovered.Measure.OfType<RawStepNode>(), node => node.TypeName.StartsWith("InstrumentComponents.OpenTap.", StringComparison.Ordinal));
            var restored = AuthoringDocumentDto.FromDraft(cold).ToDraft();
            Assert.Equal(cold.Measure.OfType<RawStepNode>().Select(node => node.XmlFragment), restored.Measure.OfType<RawStepNode>().Select(node => node.XmlFragment));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertResources(ProgramDraft imported, IReadOnlyDictionary<string, string> expected, InstrumentRef binding)
    {
        Assert.Equal(2, imported.Instruments.Count);
        var supply = imported.Instruments.Single(resource => resource.SlotName == "Supply");
        Assert.Equal(binding.TypeId, supply.TypeId); Assert.Equal(binding.VisaAddress, supply.VisaAddress);
        Assert.Equal("8123", supply.Settings["IoTimeoutMilliseconds"]); Assert.Equal(expected["Supply"], supply.OpaqueResourceXml);
        var vendor = imported.Instruments.Single(resource => resource.SlotName == "Vendor");
        Assert.Equal("Vendor.UnknownInstrument", vendor.TypeId); Assert.Equal("MOCK::VENDOR", vendor.VisaAddress);
        Assert.Equal("preserved-calibration", vendor.Settings["VendorCalibration"]); Assert.Equal(expected["Vendor"], vendor.OpaqueResourceXml);
    }
}
