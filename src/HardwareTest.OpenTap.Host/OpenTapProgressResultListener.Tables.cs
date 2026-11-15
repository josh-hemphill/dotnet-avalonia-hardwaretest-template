using OpenTap;

namespace HardwareTest.OpenTap.Host;

internal sealed partial class ProgressResultListener
{
    public override void OnResultPublished(Guid stepRunId, ResultTable result)
    {
        var producer = ResultProducer(stepRunId);
        try
        {
            if (string.Equals(result.Name, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                PublishSamples(result, stepRunId);
                return;
            }

            if (string.Equals(result.Name, "Scalar", StringComparison.OrdinalIgnoreCase))
            {
                PublishScalars(result, stepRunId);
                return;
            }

            if (string.Equals(result.Name, "MeasurementOutcome", StringComparison.OrdinalIgnoreCase))
            {
                PublishMeasurementOutcome(result, stepRunId);
                return;
            }

            if (string.Equals(result.Name, "Event", StringComparison.OrdinalIgnoreCase))
            {
                PublishEvents(result, stepRunId);
                return;
            }

            if (string.Equals(result.Name, "Identity", StringComparison.OrdinalIgnoreCase))
            {
                var idn = result.Columns.FirstOrDefault(c => c.Name == "Idn");
                var key = idn is null || idn.Data.Length == 0 ? null : Convert.ToString(idn.Data.GetValue(0));
                if (producer.Id is not null)
                {
                    _updateNode(producer.Id, producer.Name, producer.Status, producer.Verdict, key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = $"IDN {key}",
                    StepId = producer.Id,
                    StepName = producer.Name,
                    KeyValue = key,
                    StatusText = "Identity",
                });
                return;
            }

            if (string.Equals(result.Name, "Analyze", StringComparison.OrdinalIgnoreCase))
            {
                var meanCol = result.Columns.FirstOrDefault(c => c.Name == "Mean");
                var key = meanCol is null || meanCol.Data.Length == 0
                    ? null
                    : $"mean={Convert.ToDouble(meanCol.Data.GetValue(0)):F4}";
                if (producer.Id is not null)
                {
                    _updateNode(producer.Id, producer.Name, producer.Status, producer.Verdict, key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = key ?? "Analyze",
                    StepId = producer.Id,
                    StepName = producer.Name,
                    KeyValue = key,
                    StatusText = "Analyze",
                });
                return;
            }

            if (string.Equals(result.Name, "OperatorPrompt", StringComparison.OrdinalIgnoreCase))
            {
                var msgCol = result.Columns.FirstOrDefault(c => c.Name == "Message");
                var msg = msgCol is null || msgCol.Data.Length == 0
                    ? "Awaiting operator"
                    : Convert.ToString(msgCol.Data.GetValue(0));
                // Do not set AwaitingOperator here — HandleInteraction already reports the
                // authoritative pause (with InteractionRequest). Emitting a second
                // AwaitingOperator=true frame without a request races Progress handlers.
                _progress?.Report(new OpenTapProgress
                {
                    Message = msg ?? "Awaiting operator",
                    StepId = producer.Id,
                    StepName = producer.Name,
                    OperatorPromptMessage = msg,
                    StatusText = "Operator prompt noted",
                    KeyValue = msg,
                });
            }
        }
        catch
        {
            // Ignore malformed result rows.
        }
    }
}
