using System.Globalization;
using HardwareTest.Core.BenchReferences;

namespace HardwareTest.Features.Results;

public partial class ResultsViewModel
{
    /// Complete saved identity, separate from transient status and abbreviated list labels.
    public string RunIdentityText
    {
        get
        {
            var run = OpenedRun;
            if (run is null) return string.Empty;
            return string.Join(Environment.NewLine,
                $"Verdict: {run.Result}",
                $"Run ID: {Recorded(run.RunId)}",
                $"Program: {Recorded(run.PlanName)}",
                $"Program ID: {Recorded(run.PlanId)}",
                $"Session ID: {Recorded(run.SessionId)}",
                $"DUT serial: {Recorded(run.DutSerial)}",
                $"Part number: {Recorded(run.DutPartNumber)}",
                $"Revision: {Recorded(run.DutRevision)}",
                $"Operator: {Recorded(run.OperatorName)}",
                $"Started at: {(run.StartedAt == default ? "Not recorded" : run.StartedAt.ToString("O", CultureInfo.InvariantCulture))}",
                $"Completed at: {run.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "Not recorded"}",
                $"Execution: {run.ExecutionScopeText}",
                $"Backend execution: {run.ReferenceBackendResult?.ToString() ?? "Not separately recorded"}",
                BenchReferenceEvidence.Describe(run));
        }
    }

    private static string Recorded(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
}
