using System.Globalization;
using System.Text.Json.Nodes;

namespace HardwareTest.Authoring;

public sealed partial class ProcedureEditorSession
{
    public string? RecordedResults(ProcedureEditorItem item, RunDataset? recording)
    {
        if (recording is null) return null;
        var rows = DataEntries().ToArray();
        var selected = rows.FirstOrDefault(candidate => candidate.Item.Id == item.Id);
        var caseId = item.IsStep ? FindStep(item.Id).Element("CaseId")?.Value
            : selected is { Table: "cases" } ? selected.Item.Name : selected?.Parent is { Table: "cases" } owner ? owner.Item.Name : null;
        if (caseId is null) return null;
        var bands = rows.Where(row => row.Parent?.Item.Name == caseId && row.Table == "criteria")
            .Where(row => selected?.Table != "criteria" || row.Item.Id == selected.Item.Id).ToArray();
        var scores = recording.Run.Samples.Where(sample => sample.RowVerdict is not null
            && sample.EffectiveMetricKey.StartsWith(caseId + ".", StringComparison.Ordinal)).ToArray();
        var lines = new List<string>();
        foreach (var sample in scores)
        {
            var band = bands.FirstOrDefault(row => sample.EffectiveMetricKey.EndsWith("." + row.Item.Name, StringComparison.Ordinal));
            if (band is null || band.Node["low"] is not JsonValue low || band.Node["high"] is not JsonValue high) continue;
            var passed = sample.Unit == Display(band.Node["unit"]) && sample.Value >= low.GetValue<double>() && sample.Value <= high.GetValue<double>();
            var count = recording.Run.Samples.Count(point => point.SampleIndex is not null && point.StepRunId == sample.StepRunId
                && point.ProducerStepId == sample.ProducerStepId && point.EffectiveMetricKey.StartsWith(caseId + ".capture.", StringComparison.Ordinal));
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{sample.EffectiveMetricKey} · {sample.Timestamp:O} · {sample.Value:G} {sample.Unit}\nRecorded {sample.RowVerdict} · draft band [{low.GetValue<double>():G}, {high.GetValue<double>():G}] {(passed ? "Pass" : "Fail")} · {count} capture points"));
        }
        return lines.Count == 0 ? null : $"Recording {recording.Run.RunId} · limits applied to recorded values\n" + string.Join("\n\n", lines);
    }
}
