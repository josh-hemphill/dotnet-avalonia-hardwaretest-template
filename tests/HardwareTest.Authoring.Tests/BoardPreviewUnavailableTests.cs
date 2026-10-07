using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class BoardPreviewUnavailableTests
{
    [Theory]
    [InlineData("<TestStep type='Missing.Plugin.Step'><Name>Imported unknown step</Name></TestStep>")]
    [InlineData("<TestStep")]
    public void Opaque_or_malformed_imported_source_stays_unavailable_without_crashing_the_board(string xml)
    {
        var raw = new RawStepNode("Missing.Plugin.Step", xml);
        var input = new MetricNode(new MetricDraft("Known input", "known", "timeseries", "V", null, null,
            new MeasureSource("DMM", AuthoringFunctionIds.BasicAcquireVoltage, new Dictionary<string, string>())));
        var draft = MockDmmDraftFixture.Create("opaque-preview") with { Measure = [raw, input] };
        var before = raw.XmlFragment;
        var board = BoardPreviewBuilder.Build(draft);
        var unavailable = Assert.Single(board, tile => tile.NodeId == raw.NodeId);
        Assert.Empty(unavailable.Preview.CannedSamples);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Preview.Note));
        Assert.NotEmpty(Assert.Single(board, tile => tile.NodeId == input.NodeId).Preview.CannedSamples);
        Assert.Equal(before, raw.XmlFragment);
    }
}
