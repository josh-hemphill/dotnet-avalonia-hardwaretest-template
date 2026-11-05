using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Text;
using HardwareTest.OpenTap.Host;
using StepFilter = HardwareTest.Features.RunTest.StepStatusFilter;

namespace HardwareTest.Features.RunTest;

/// Reference acceptance, terminal presentation, immutable attempt evidence and reports.
public sealed partial class RunExecutionViewModel
{
    private static OpenTapRunSummary WithReferenceOutcome(OpenTapRunSummary summary, IReadOnlyList<BenchReferenceEvaluation> evaluations)
    {
        var failed = evaluations.Any(evaluation => !evaluation.Passed);
        var steps = summary.Steps.ToList();
        foreach (var evaluation in evaluations)
            steps.Add(new StepResultRecord
            {
                StepId = "reference:" + evaluation.RuleId,
                StepType = "Bench reference acceptance",
                StepPath = $"Reference acceptance/{evaluation.MetricKey} [{evaluation.RuleId}]",
                Passed = evaluation.Passed,
                Message = evaluation.Message,
                StartedAt = summary.StartedAt,
                CompletedAt = summary.CompletedAt,
            });
        return new OpenTapRunSummary
        {
            RunId = summary.RunId,
            PlanName = summary.PlanName,
            Result = failed && summary.Result == RunResult.Passed ? RunResult.Failed : summary.Result,
            ErrorMessage = failed ? "Reference acceptance failed: " + string.Join(" ", evaluations.Where(evaluation => !evaluation.Passed).Select(evaluation => evaluation.Message)) : summary.ErrorMessage,
            DutSerial = summary.DutSerial,
            DutPartNumber = summary.DutPartNumber,
            DutRevision = summary.DutRevision,
            SessionId = summary.SessionId,
            OperatorName = summary.OperatorName,
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
            Samples = summary.Samples,
            Events = summary.Events,
            Steps = steps,
            Verdict = failed ? "Fail" : summary.Verdict,
        };
    }

    private void PublishRunOutcome(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        _host.SyncHierarchyLive();
        _stepTree.ApplyStepResults(summary.Steps);
        _stepTree.ApplyAttemptTexts();
        if (summary.Result == RunResult.Passed && !_stepTree.HasFailedLeaves && _stepTree.IsFilteredToFail)
            _stepTree.StepStatusFilter = StepFilter.All;
        if (!selectionOnly && summary.Result == RunResult.Failed)
        {
            _stepTree.ClearScope();
            _stepTree.StepStatusFilter = StepFilter.Fail;
            _stepTree.MaybeAutoFocusFail();
        }

        if (!string.IsNullOrWhiteSpace(selectionPath))
        {
            _stepTree.ResolveSelectedStep(selectionPath);
        }

        _host.OpenSelectedDetail(revealDetail: false);
        _host.Status = BuildCompletionStatus(selectionOnly, selectionPath, selectionName, summary);
        var completionSeverity = summary.Result switch
        {
            RunResult.Passed => RunBannerSeverity.Info,
            RunResult.Failed or RunResult.Error => RunBannerSeverity.Error,
            _ => RunBannerSeverity.Warning,
        };
        if (_host is RunTestViewModel completedBoard) completedBoard.SetRunCompletionBanner(completionSeverity, _host.Status);
        else _host.SetBanner(completionSeverity, _host.Status);
        _stepDetail.AttemptSummaryChip = string.Empty;
        if (selectionOnly
            && !string.IsNullOrWhiteSpace(selectionPath)
            && _attemptLedger.TryGetValue(selectionPath, out var ledger))
        {
            _stepDetail.AttemptSummaryChip = ledger.Display;
        }
    }

    private async Task<DutHistoryReport?> AnalyzeHistoryAsync(TestRunRecord record, RunResult result)
    {
        if (_dutHistory is null || result is not (RunResult.Passed or RunResult.Failed))
        {
            return null;
        }

        try
        {
            var report = await _dutHistory.AnalyzeAsync(record).ConfigureAwait(false);
            await _host.RunOnUiAsync(() =>
            {
                if (!_settings.ShowDutHistoryOnRun)
                {
                    return;
                }

                _host.HistoryBanner = report.OperatorSummary;
                if (!string.IsNullOrWhiteSpace(report.OperatorSummary))
                {
                    _host.Status += " " + report.OperatorSummary;
                }
            }).ConfigureAwait(false);
            return report;
        }
        catch (Exception ex)
        {
            if (_settings.ShowDutHistoryOnRun)
            {
                await _host.RunOnUiAsync(() => _host.HistoryBanner = $"DUT history unavailable: {ex.Message}")
                    .ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task GenerateReportsAsync(
        TestRunRecord record,
        ProgramItemViewModel program,
        RunResult result,
        DutHistoryReport? historyReport)
    {
        if (result is not (RunResult.Passed or RunResult.Failed))
        {
            return;
        }

        try
        {
            var kinds = program.ReportKinds is { Count: > 0 }
                ? program.ReportKinds
                : ProgramCatalog.ResolveReportKinds(program.Id);
            await _reportService.GenerateReportsAsync(record, kinds, historyReport).ConfigureAwait(false);
            await _host.RunOnUiAsync(() => _host.Status += " Report(s) generated.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _host.RunOnUiAsync(() => _host.Status += $" Report failed: {ex.Message}").ConfigureAwait(false);
        }
    }

    private string BuildCompletionStatus(
        bool selectionOnly,
        string? selectionPath,
        string? selectionName,
        OpenTapRunSummary summary)
    {
        if (selectionOnly && !string.IsNullOrWhiteSpace(selectionPath))
        {
            _attemptLedger.TryGetValue(selectionPath, out var ledger);
            var attemptNo = ledger?.AttemptCount ?? 1;
            var badge = ledger?.Display ?? $"{attemptNo}";
            return $"Attempt #{attemptNo} for {selectionName ?? selectionPath} ({badge}). Snapshot also saved to Results.";
        }

        return $"Suite finished: {summary.Result}. Session attempts rolled up; Results entry {ShortId.Display(summary.RunId)}.";
    }

    private void RecordAttempts(OpenTapRunSummary summary)
    {
        foreach (var step in summary.Steps)
        {
            var path = string.IsNullOrWhiteSpace(step.StepPath) ? step.StepType : step.StepPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!_attemptLedger.TryGetValue(path, out var ledger))
            {
                ledger = new StepAttemptSummary
                {
                    StepPath = path,
                    StepName = step.StepType,
                };
                _attemptLedger[path] = ledger;
            }

            ledger.Attempts.Add(new StepResultRecord
            {
                StepId = step.StepId,
                StepType = step.StepType,
                StepPath = path,
                AttemptNumber = ledger.AttemptCount + 1,
                Passed = step.Passed,
                Message = step.Message,
                StartedAt = step.StartedAt,
                CompletedAt = step.CompletedAt,
            });
            ledger.AttemptCount++;
            if (step.Passed)
            {
                ledger.PassedCount++;
            }
            else
            {
                ledger.FailedCount++;
            }

            ledger.LatestPassed = step.Passed;
            ledger.LatestMessage = step.Message;
            ledger.StepName = step.StepType;
        }
    }

    private List<StepResultRecord> BuildRolledUpSteps()
        => _attemptLedger.Values
            .Select(l => l.Attempts.LastOrDefault())
            .Where(a => a is not null)
            .Cast<StepResultRecord>()
            .OrderBy(a => a.StepPath)
            .ToList();
}
