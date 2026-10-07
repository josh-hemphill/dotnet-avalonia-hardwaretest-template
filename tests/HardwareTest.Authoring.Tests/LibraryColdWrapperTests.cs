using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class LibraryColdWrapperTests
{
    [Theory]
    [InlineData("disabled-group", true)]
    [InlineData("disabled-group", false)]
    [InlineData("group-in-repeat", true)]
    [InlineData("group-in-repeat", false)]
    [InlineData("disabled-repeat", true)]
    [InlineData("disabled-repeat", false)]
    public async Task Cold_opaque_wrappers_preserve_lifecycle_source_and_refuse_phase_movement_after_preparation(string shape, bool identity)
    {
        var package = Environment.GetEnvironmentVariable("HARDWARETEST_LIBRARY_TEST_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(package)) Assert.Skip("Actual upstream package required; each wrapper imports in a fresh library-free process.");
        var root = Path.Combine(Path.GetTempPath(), "ht-library-wrapper-" + Guid.NewGuid().ToString("N"));
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
            var binding = new InstrumentRef("Supply", supply.TypeId, "TCPIP0::192.0.2.8::inst0::INSTR");
            var draft = new AuthoringPlanInitializer().Construct(new("wrapped")
            { Home = home, Instruments = [binding], IdentityInstrumentSlot = identity ? "Supply" : null, IncludeSafeShutdown = !identity }).Draft;
            var path = Path.Combine(root, "wrapped.TapPlan");
            new PlanCompiler(selectedHome: home).Save(draft, path);
            var xml = XDocument.Load(path);
            var lifecycle = xml.Descendants().Where(element => element.Name.LocalName == "TestStep"
                && ((string?)element.Attribute("type"))?.Contains("InstrumentComponents.OpenTap.", StringComparison.Ordinal) == true).ToArray();
            Assert.Single(lifecycle);
            var wrapperId = Guid.NewGuid();
            var wrapper = new XElement("TestStep", new XAttribute("type", typeof(TestGroupStep).FullName!), new XAttribute("Id", wrapperId),
                new XElement("Name", "Preserved lifecycle wrapper"), new XElement("ChildTestSteps", lifecycle.Select(step => new XElement(step))));
            if (shape == "disabled-group") wrapper.Add(new XElement("Enabled", "false"));
            XElement outer = wrapper;
            if (shape != "disabled-group")
            {
                outer = new XElement("TestStep", new XAttribute("type", typeof(RepeatLoopStep).FullName!), new XAttribute("Id", Guid.NewGuid()),
                    new XElement("Count", 2), new XElement("Name", "Preserved repeat"), new XElement("ChildTestSteps", wrapper));
                if (shape == "disabled-repeat") outer.Add(new XElement("Enabled", "false"));
            }
            // Preserve the original setup or cleanup position; only add the opaque wrapper.
            lifecycle[0].ReplaceWith(outer); xml.Save(path);
            var original = File.ReadAllBytes(path);
            var coldPath = Path.Combine(root, "cold", "wrapped.authoring.json"); Directory.CreateDirectory(Path.GetDirectoryName(coldPath)!);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll"));
            start.ArgumentList.Add("--cold-library-import"); start.ArgumentList.Add(path); start.ArgumentList.Add(coldPath);
            using var child = Process.Start(start)!;
            try
            {
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(child.ExitCode == 0, await error); Assert.Contains("cold-library-source-preserved", await output);
            }
            finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
            var cold = JsonSerializer.Deserialize(File.ReadAllBytes(coldPath), AuthoringDocumentJsonContext.Default.AuthoringDocumentDto)!.ToDraft();
            var raw = shape == "group-in-repeat"
                ? Assert.IsType<RawStepNode>(Assert.Single(Assert.IsType<RepeatNode>(Assert.Single(cold.Measure)).Children))
                : Assert.IsType<RawStepNode>(Assert.Single(cold.Measure));
            var expected = shape == "group-in-repeat" ? wrapper : outer;
            Assert.Equal(Guid.Parse((string)expected.Attribute("Id")!), raw.NodeId);
            Assert.Equal((string)expected.Attribute("type")!, raw.TypeName);
            Assert.Equal(expected.ToString(SaveOptions.DisableFormatting), raw.XmlFragment);
            foreach (var step in lifecycle)
            {
                Assert.Contains((string)step.Attribute("Id")!, raw.XmlFragment);
                Assert.Contains("Supply", raw.XmlFragment);
            }
            var source = File.ReadAllBytes(coldPath);
            var restored = AuthoringDocumentDto.FromDraft(cold).ToDraft();
            Assert.Contains(AuthoringIssueService.GetIssues(restored, home), issue => issue.Code == "LIBRARY_LIFECYCLE_REIMPORT" && issue.NodeId == raw.NodeId);
            var errorAfterPreparation = Assert.Throws<AuthoringWorkspaceException>(() => new PlanCompiler(selectedHome: home).Save(restored, path));
            Assert.Contains("LIBRARY_LIFECYCLE_REIMPORT", errorAfterPreparation.Message);
            Assert.Equal(original, File.ReadAllBytes(path)); Assert.Equal(source, File.ReadAllBytes(coldPath));
        }
        finally { Directory.Delete(root, true); }
    }
}
