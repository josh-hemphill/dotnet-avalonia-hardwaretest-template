using System.Collections.Concurrent;
using HardwareTest.Core.Runs;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

internal sealed partial class ProgressResultListener
{
    private readonly ConcurrentDictionary<Guid, string> _measurementOutcomes = new();
    private readonly ConcurrentDictionary<Guid, StepResultRecord> _completedSteps = new();

    private void PublishMeasurementOutcome(ResultTable table, Guid runId)
    {
        string Read(string name)
        {
            return Convert.ToString(table.Columns.Single(column => column.Name == name).Data.GetValue(0)) ?? "";
        }

        var status = Read("Status");
        if (status != "Overload") return;
        var producer = ResultProducer(runId);
        var detail = $"{Read("Channel")} — Overload: no valid measurement ({Read("Unit")}). {Read("Driver")} [{Read("Resource")}] / {Read("Operation")}. Check the connection, signal and configured range before retrying.";
        _measurementOutcomes[runId] = detail;
        // Result delivery may precede or follow the completion callback.
        if (_completedSteps.TryGetValue(runId, out var completed)) completed.OutcomeDetail = detail;
        if (producer.Id is not null) _updateNode(producer.Id, producer.Name, "Fail", "Fail", detail);
        _progress?.Report(new OpenTapProgress
        {
            Message = detail,
            StepId = producer.Id,
            StepName = producer.Name,
            StepPath = _resolvePath(producer.Id ?? "", producer.Name),
            KeyValue = detail,
            StatusText = "Fail",
            Verdict = "Fail"
        });
    }
}
