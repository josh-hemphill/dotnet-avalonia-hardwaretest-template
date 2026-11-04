using System.Text.Json;
using HardwareTest.Features.RunTest;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.E2E.Tests;

/// A persisted mock-only plan, discovered through the normal production catalog.
internal sealed class OperatorReviewPlanFixture : IDisposable
{
    internal OperatorReviewPlanFixture()
    {
        Id = "operator-review-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(AppContext.BaseDirectory, "Programs");
        Directory.CreateDirectory(directory);
        PlanPath = Path.Combine(directory, Id + ".TapPlan");
        SidecarPath = Path.ChangeExtension(PlanPath, ".program.json");
        try
        {
            var plan = SampleProgramFactory.Create();
            var steps = Flatten(plan.ChildTestSteps).ToArray();
            steps.OfType<AcquireVoltageStep>().Single().Name = LongAcquisitionName;
            steps.OfType<MeanGteStep>().Single().Threshold = 1_000_000;
            var prompt = steps.OfType<OperatorInputStep>().Single();
            prompt.Title = "Verify the fixture identity and measured installation torque before continuing";
            prompt.Message = string.Join("\n", Enumerable.Range(1, 12).Select(index =>
                $"Fixture check {index}: verify the connector orientation and strain relief against the station instruction sheet."));
            prompt.StringFieldLabel = "Fixture asset identity from the installation record";
            prompt.StringFieldRequired = true;
            prompt.NumberFieldLabel = "Measured fixture installation torque (N·m)";
            prompt.NumberFieldRequired = true;
            plan.Save(PlanPath);
            File.WriteAllText(SidecarPath, JsonSerializer.Serialize(new ProgramSidecar
            {
                DisplayName = "Fixture verification and voltage acceptance review",
                DutFamily = "review-controller",
                RequiredFields = [RequiredFieldIds.Serial, RequiredFieldIds.PartNumber, RequiredFieldIds.Revision, RequiredFieldIds.Operator],
                ReportKinds = [HardwareTest.Core.Runs.ReportKinds.Status],
            }, ProgramCatalogJsonContext.Default.ProgramSidecar));
        }
        catch
        {
            // Construction failures must not leave a partial plan/sidecar in the catalog.
            Dispose();
            throw;
        }
    }

    internal string Id { get; }
    internal string PlanPath { get; }
    internal string SidecarPath { get; }
    internal const string LongAcquisitionName = "Acquire diagnostic voltage for the main controller power-input rail after fixture verification and thermal stabilization";

    internal async Task SelectAsync(RunTestViewModel run)
    {
        await run.ProgramSelection.RefreshProgramsCommand.ExecuteAsync();
        run.ProgramSelection.SelectedProgram = Assert.Single(run.ProgramSelection.Programs, program => program.Id == Id);
        await E2EHarness.WaitUntilAsync(() => run.IsSelectedProgramReady && FlattenHierarchy(run.StepTree.Hierarchy)
            .Any(step => step.Name == LongAcquisitionName), TimeSpan.FromSeconds(30), "Persisted review fixture did not finish loading through the normal catalog.");
        Assert.True(run.SessionPanel.RequirePartNumber);
        Assert.True(run.SessionPanel.RequireRevision);
    }

    internal static IEnumerable<HierarchyStepViewModel> FlattenHierarchy(IEnumerable<HierarchyStepViewModel> roots)
    {
        foreach (var step in roots)
        {
            yield return step;
            foreach (var child in FlattenHierarchy(step.Children)) yield return child;
        }
    }

    private static IEnumerable<ITestStep> Flatten(IEnumerable<ITestStep> roots)
    {
        foreach (var step in roots)
        {
            yield return step;
            foreach (var child in Flatten(step.ChildTestSteps)) yield return child;
        }
    }

    public void Dispose()
    {
        File.Delete(PlanPath);
        File.Delete(SidecarPath);
    }
}
