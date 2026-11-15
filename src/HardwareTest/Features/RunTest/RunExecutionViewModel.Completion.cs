using System;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Text;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Features.RunTest;

/// Completion copy keeps device diagnostics visible while bounding the shell strip.
public sealed partial class RunExecutionViewModel
{
    private string BuildCompletionStatus(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        string status;
        if (selectionOnly && !string.IsNullOrWhiteSpace(selectionPath))
        {
            _attemptLedger.TryGetValue(selectionPath, out var ledger);
            var attemptNo = ledger?.AttemptCount ?? 1;
            var badge = ledger?.Display ?? $"{attemptNo}";
            status = $"Attempt #{attemptNo} for {selectionName ?? selectionPath} ({badge}). Snapshot also saved to Results.";
        }

        else status = $"Suite finished: {summary.Result}. Session attempts rolled up; Results entry {ShortId.Display(summary.RunId)}.";
        if (!string.IsNullOrWhiteSpace(summary.ErrorMessage)
            && (summary.Result == RunResult.Error || summary.ErrorMessage.Contains("Device I/O failed", StringComparison.Ordinal)))
        {
            var detail = summary.ErrorMessage.Length > 220 ? summary.ErrorMessage[..220] + "…" : summary.ErrorMessage;
            status += " " + detail + " Open Results for the full error and recovery details.";
        }
        return status;
    }

}
