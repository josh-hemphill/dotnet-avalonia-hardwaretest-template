using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class ImportedProcedureTests
{
    [Fact]
    public void Unavailable_plugin_tree_remains_inspectable_and_blocks_lossy_export()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "fixture.TapPlan");
            var xml = """
                <TestPlan type="OpenTap.TestPlan">
                  <Steps>
                    <TestStep type="Unknown.CustomFixture" Id="1175fbe1-f461-49cb-aa29-8ea0e20c51c8">
                      <Name>Opaque fixture</Name>
                      <ChildTestSteps>
                        <TestStep type="Unknown.CustomMeasurement" Id="3db50fca-bc9a-4073-8e6e-80731fdd06f2"><Name>Unknown test</Name></TestStep>
                      </ChildTestSteps>
                    </TestStep>
                  </Steps>
                </TestPlan>
                """;
            File.WriteAllText(path, xml);
            var compiler = new PlanCompiler();
            var draft = compiler.Load(path);
            var restored = AuthoringDocumentDto.FromDraft(draft).ToDraft();
            Assert.Equal(xml, restored.ImportedPlanXml);
            var editor = new ProcedureEditorSession(restored, path, new AuthoringManifest());
            Assert.False(editor.CanEdit);
            Assert.Equal(["Opaque fixture", "Unknown test"], editor.Steps().Select(step => step.Name));
            Assert.Contains("plugin", editor.InspectionReason, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<AuthoringWorkspaceException>(() => compiler.Save(restored, path));
            Assert.Equal(xml, File.ReadAllText(path));
            Assert.False(File.Exists(PlanCompiler.SidecarPath(path)));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Program_assets_follow_program_inclusion_and_exclude_undeclared_files()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new AuthoringManifest
            {
                SchemaVersion = 2,
                Package = new() { Name = "Asset test" },
                ProgramAssets = new()
                {
                    ["first"] = ["data/routes.json", "data/shared.json"],
                    ["second"] = ["data/other.json", "data/shared.json"]
                },
                ExcludedProgramIds = ["second"]
            };
            AuthoringWorkspaceLoader.SaveManifest(directory.FullName, manifest);
            var plans = directory.CreateSubdirectory("plans");
            File.WriteAllText(Path.Combine(plans.FullName, "first.TapPlan"), "");
            File.WriteAllText(Path.Combine(plans.FullName, "second.TapPlan"), "");
            File.WriteAllText(Path.Combine(plans.CreateSubdirectory("data").FullName, "undeclared.json"), "");
            var workspace = AuthoringWorkspaceLoader.Load(directory.FullName);
            Assert.Equal(["first.TapPlan", "data/routes.json", "data/shared.json"], PackageXmlRenderer.EnumeratePackFiles(workspace));
            manifest.ProgramAssets["first"] = ["data/../private.json"];
            Assert.Throws<AuthoringWorkspaceException>(() => AuthoringProgramAssets.Validate(manifest));
        }
        finally { directory.Delete(recursive: true); }
    }
}
