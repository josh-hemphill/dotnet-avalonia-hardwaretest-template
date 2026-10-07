using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringIsolatedImpactMutationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-isolated-impact-" + Guid.NewGuid().ToString("N"));

    public AuthoringIsolatedImpactMutationTests()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dirs.proj"))) directory = directory.Parent;
        Directory.CreateDirectory(_root);
        File.Copy(Path.Combine(directory!.FullName, "plans", "opentap", "authoring.json"), Path.Combine(_root, "authoring.json"));
    }

    [Theory]
    [InlineData("expression-input")]
    [InlineData("expression-source")]
    [InlineData("transfer-input")]
    [InlineData("transfer-numerator")]
    [InlineData("transfer-denominator")]
    [InlineData("transfer-period")]
    [InlineData("transfer-method")]
    [InlineData("limit-low")]
    [InlineData("limit-high")]
    [InlineData("limit-threshold")]
    [InlineData("history-enabled")]
    [InlineData("history-watch")]
    [InlineData("history-alert")]
    [InlineData("metric-name")]
    [InlineData("metric-channel")]
    [InlineData("metric-role")]
    [InlineData("metric-unit")]
    [InlineData("identity-slot")]
    [InlineData("prompt-name")]
    [InlineData("prompt-message")]
    [InlineData("input-name")]
    [InlineData("input-title")]
    [InlineData("input-message")]
    [InlineData("input-string-field")]
    [InlineData("input-number-field")]
    [InlineData("measure-identity")]
    [InlineData("setup-identity")]
    [InlineData("cleanup-identity")]
    public void A_single_nested_value_change_invalidates_the_reviewed_removal(string change)
    {
        var vm = new AuthoringWorkspaceViewModel();
        vm.Open(_root);
        vm.CreateDemoProgram("a");
        var original = vm.SelectedProgram! with
        {
            Setup = [new IdentitySetup("DMM"), new OperatorPromptSetup("prompt", "confirm"), new OperatorInputSetup("input", "title", "enter", "serial", "reading")],
            Measure = [new RepeatNode(2, [
                new MetricNode(new MetricDraft("expression", "derived", "waveform", "V", new LimitSpec(0, 10, 5), new HistorySpec(true, 10, 20), new ExpressionAlgorithm(["source"], "mean(source)"))),
                new MetricNode(new MetricDraft("filter", "filtered", "waveform", "V", null, null, new TransferFunctionAlgorithm("source", [1, 2], [1, 3], 0.1, "zoh"))),
            ])],
        };
        vm.ReplaceSelected(original);
        var reviewed = vm.PrepareSelectedProgramRemoval();
        var changed = ChangeOneValue(vm.SelectedProgram!, change);
        vm.ReplaceSelected(changed);
        var before = vm.SelectedProgram;
        var files = Directory.EnumerateFiles(_root).ToDictionary(path => path, File.ReadAllBytes);

        Assert.Throws<AuthoringWorkspaceException>(() => vm.ApplyProgramRemoval(reviewed));

        Assert.Same(before, vm.SelectedProgram);
        Assert.Equal("a", Assert.Single(vm.Programs).PlanId);
        foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    private static ProgramDraft ChangeOneValue(ProgramDraft program, string change)
    {
        var setup = program.Setup.ToArray();
        var repeat = Assert.IsType<RepeatNode>(Assert.Single(program.Measure));
        var children = repeat.Children.ToArray();
        var node = Assert.IsType<MetricNode>(children[0]);
        var metric = node.Metric;
        var expression = Assert.IsType<ExpressionAlgorithm>(metric.Source);
        var transferNode = Assert.IsType<MetricNode>(children[1]);
        var transfer = Assert.IsType<TransferFunctionAlgorithm>(transferNode.Metric.Source);
        switch (change)
        {
            case "measure-identity": node = node with { NodeId = Guid.NewGuid() }; break;
            case "setup-identity": setup[0] = Assert.IsType<IdentitySetup>(setup[0]) with { NodeId = Guid.NewGuid() }; break;
            case "cleanup-identity": return program with { Cleanup = program.Cleanup with { NodeId = Guid.NewGuid() } };
            case "expression-input": metric = metric with { Source = expression with { InputChannelKeys = ["other"] } }; break;
            case "expression-source": metric = metric with { Source = expression with { Source = "mean(source)+1" } }; break;
            case "transfer-input": transfer = transfer with { InputChannelKey = "other" }; break;
            case "transfer-numerator": transfer = transfer with { Numerator = [1, 4] }; break;
            case "transfer-denominator": transfer = transfer with { Denominator = [1, 4] }; break;
            case "transfer-period": transfer = transfer with { TsSeconds = 0.2 }; break;
            case "transfer-method": transfer = transfer with { Method = "tustin" }; break;
            case "limit-low": metric = metric with { Limits = metric.Limits! with { Low = -1 } }; break;
            case "limit-high": metric = metric with { Limits = metric.Limits! with { High = 11 } }; break;
            case "limit-threshold": metric = metric with { Limits = metric.Limits! with { Threshold = 6 } }; break;
            case "history-enabled": metric = metric with { History = metric.History! with { Enabled = false } }; break;
            case "history-watch": metric = metric with { History = metric.History! with { WatchPercent = 11 } }; break;
            case "history-alert": metric = metric with { History = metric.History! with { AlertPercent = 21 } }; break;
            case "metric-name": metric = metric with { Name = "renamed" }; break;
            case "metric-channel": metric = metric with { ChannelKey = "renamed" }; break;
            case "metric-role": metric = metric with { DisplayRole = "scalar" }; break;
            case "metric-unit": metric = metric with { YUnit = "A" }; break;
            case "identity-slot": setup[0] = Assert.IsType<IdentitySetup>(setup[0]) with { InstrumentSlot = "OTHER" }; break;
            case "prompt-name": setup[1] = Assert.IsType<OperatorPromptSetup>(setup[1]) with { Name = "renamed" }; break;
            case "prompt-message": setup[1] = Assert.IsType<OperatorPromptSetup>(setup[1]) with { Message = "changed" }; break;
            case "input-name": setup[2] = Assert.IsType<OperatorInputSetup>(setup[2]) with { Name = "renamed" }; break;
            case "input-title": setup[2] = Assert.IsType<OperatorInputSetup>(setup[2]) with { Title = "changed" }; break;
            case "input-message": setup[2] = Assert.IsType<OperatorInputSetup>(setup[2]) with { Message = "changed" }; break;
            case "input-string-field": setup[2] = Assert.IsType<OperatorInputSetup>(setup[2]) with { StringFieldId = "other" }; break;
            case "input-number-field": setup[2] = Assert.IsType<OperatorInputSetup>(setup[2]) with { NumberFieldId = "other" }; break;
            default: throw new ArgumentException("Unknown mutation", nameof(change));
        }
        children[0] = node with { Metric = metric };
        children[1] = transferNode with { Metric = transferNode.Metric with { Source = transfer } };
        return program with { Setup = setup, Measure = [repeat with { Children = children }] };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
