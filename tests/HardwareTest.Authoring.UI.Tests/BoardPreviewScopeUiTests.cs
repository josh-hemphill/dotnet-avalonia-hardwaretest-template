using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.OpenTap.Host;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class BoardPreviewScopeUiTests
{
    [AvaloniaFact]
    public void Actual_inspector_recording_evidence_and_status_follow_same_output_formula_selection()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        MetricNode Input(string key) => new(new MetricDraft(key, key, PresentationRoles.Timeseries, "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string> { ["SampleCount"] = "3", ["IntervalMs"] = "5" })));
        MetricNode Mean(string input) => new(new MetricDraft(input + " mean", "result", PresentationRoles.Scalar, "V", new LimitSpec(null, null, 0), null, new ExpressionAlgorithm([input], $"mean({input})")));
        var firstInput = Input("first");
        var secondInput = Input("second");
        var first = Mean("first");
        var second = Mean("second");
        var draft = new PlanCompiler().Load(Path.Combine(fixture.WorkspaceRoot, "sample.TapPlan")) with
        { Measure = [new RepeatNode(2, [firstInput, first]), new RepeatNode(1, [secondInput, second])] };
        var document = AuthoringDocumentDto.FromDraft(draft);
        document.RequiresCompilation = true;
        new AuthoringDocumentStore(fixture.WorkspaceRoot).Save(document);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        // Synthetic source-only editing fixture: duplicate output channels stay deployment-invalid.
        var recording = new TestRunRecord
        {
            PlanId = draft.PlanId,
            Samples = [
            new StoredSample { MetricKey = "first", ProducerStepId = firstInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 2 },
            new StoredSample { MetricKey = "first", ProducerStepId = firstInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 2, Value = 4 },
            new StoredSample { MetricKey = "second", ProducerStepId = secondInput.NodeId, StepRunId = Guid.NewGuid(), IterationIndex = 1, Value = 15 }]
        };
        var path = Path.Combine(fixture.WorkspaceRoot, "scoped.json");
        File.WriteAllText(path, JsonSerializer.Serialize(recording, AppJsonContext.Default.TestRunRecord));
        vm.ImportRecording(path, "scoped");
        void Select(Guid id)
        {
            vm.SelectSequence(vm.SequenceItems.ToList().FindIndex(row => row.NodeId == id));
            AuthoringUiFixture.Drain();
        }
        var evidence = fixture.Control<TextBlock>("Selected recording formula evidence");
        var status = fixture.Control<TextBlock>("Formula deployment status");
        Select(first.NodeId);
        Assert.Contains("2 source execution(s)", evidence.Text);
        Assert.Equal("Deployable recipe", status.Text);
        Select(second.NodeId);
        Assert.Contains("1 source execution(s)", evidence.Text);
        vm.FormulaSource = "mean(missing)";
        AuthoringUiFixture.Drain();
        Assert.Contains("0 source execution(s)", evidence.Text);
        Assert.Contains("missing", evidence.Text);
        Assert.Equal("Missing deployment requirements", status.Text);
        Select(first.NodeId);
        Assert.Contains("2 source execution(s)", evidence.Text);
        Assert.DoesNotContain("missing", evidence.Text);
        Assert.Equal("Deployable recipe", status.Text);
        Assert.Equal(4, vm.Preview.CannedValue);
    }
}
