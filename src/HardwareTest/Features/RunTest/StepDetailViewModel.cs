using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HardwareTest.Core.Runs;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Bottom-tray step detail: key/values, attempt history, session log lines and region toggles.
public partial class StepDetailViewModel : ReactiveObject
{
    private const int DetailCap = 200;

    /// <param name="openSelectedStepDetail">
    /// Coordinator hook that re-opens the currently selected step; the child does not know the selection.
    /// </param>
    /// <param name="closeDetail">Coordinator hook that returns to the Steps workspace.</param>
    public StepDetailViewModel(Action? openSelectedStepDetail = null, Action? closeDetail = null)
    {
        OpenStepDetailCommand = ReactiveCommand.Create(() => openSelectedStepDetail?.Invoke());
        CloseDetailCommand = ReactiveCommand.Create(() =>
        {
            ShowDetailRegion = false;
            closeDetail?.Invoke();
        });
        ToggleDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (ShowDetailRegion)
            {
                ShowDetailRegion = false;
                closeDetail?.Invoke();
                return;
            }

            openSelectedStepDetail?.Invoke();
        });
        SelectPaneCommand = ReactiveCommand.Create<string>(pane =>
        {
            if (!string.IsNullOrWhiteSpace(pane))
            {
                SelectedPane = pane;
            }
        });
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedPane))
            {
                this.RaisePropertyChanged(nameof(IsSummaryPane));
                this.RaisePropertyChanged(nameof(IsMeasurementsPane));
                this.RaisePropertyChanged(nameof(IsSetupPane));
                this.RaisePropertyChanged(nameof(IsLogPane));
            }
        };
    }

    public ObservableCollection<string> DetailLines { get; } = [];
    public ObservableCollection<string> DetailKeyValues { get; } = [];
    public ObservableCollection<string> AttemptHistoryLines { get; } = [];

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenStepDetailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CloseDetailCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleDetailsCommand { get; }
    public ReactiveCommand<string, System.Reactive.Unit> SelectPaneCommand { get; }

    [Reactive] private HierarchyStepViewModel? _detailStep;
    [Reactive] private bool _showDetailRegion;
    [Reactive] private string _selectedPane = StepDetailPane.Summary;
    [Reactive] private bool _showLiveLog;
    [Reactive] private bool _showDetails = true;
    [Reactive] private bool _sessionLogExpanded;
    [Reactive] private string _attemptSummaryChip = string.Empty;
    [Reactive] private string _detailChipText = "Pending";
    [Reactive] private string _detailPrimaryLine = string.Empty;
    [Reactive] private string _conditionSummary = string.Empty;

    public bool IsSummaryPane => string.Equals(SelectedPane, StepDetailPane.Summary, StringComparison.Ordinal);
    public bool IsMeasurementsPane => string.Equals(SelectedPane, StepDetailPane.Measurements, StringComparison.Ordinal);
    public bool IsSetupPane => string.Equals(SelectedPane, StepDetailPane.Setup, StringComparison.Ordinal);
    public bool IsLogPane => string.Equals(SelectedPane, StepDetailPane.Log, StringComparison.Ordinal);

    /// Rebuilds the detail pane for one step; caller supplies the cross-cluster inputs.
    public void Show(
        HierarchyStepViewModel step,
        IEnumerable<InteractionFieldViewModel> parameterFields,
        StepAttemptSummary? ledger,
        string? conditionSummary,
        bool revealDetail)
    {
        DetailStep = step;
        if (revealDetail)
        {
            ShowDetailRegion = true;
        }

        ShowDetails = true;
        DetailChipText = StatusChip.FromStatus(step.StatusText, step.Verdict);
        DetailPrimaryLine = !string.IsNullOrWhiteSpace(step.KeyValue)
            ? step.KeyValue!
            : step.StatusText;
        DetailKeyValues.Clear();
        if (!string.IsNullOrWhiteSpace(step.KeyValue))
        {
            DetailKeyValues.Add(step.KeyValue);
        }

        DetailKeyValues.Add($"Status: {step.StatusText}");
        DetailKeyValues.Add($"Verdict: {step.Verdict}");
        DetailKeyValues.Add($"Path: {step.Path}");
        foreach (var field in parameterFields.Where(f =>
                     f.Label.Contains("Presentation", StringComparison.OrdinalIgnoreCase)))
        {
            DetailKeyValues.Add(field.Label + ": " + field.Value);
        }

        if (!string.IsNullOrWhiteSpace(step.AttemptsText))
        {
            DetailKeyValues.Add($"Attempts: {step.AttemptsText}");
        }

        AttemptHistoryLines.Clear();
        if (ledger is not null)
        {
            AttemptSummaryChip = ledger.Display;
            foreach (var attempt in ledger.Attempts)
            {
                AttemptHistoryLines.Add(
                    $"#{attempt.AttemptNumber} {(attempt.Passed ? "PASS" : "FAIL")} — {attempt.Message} @ {attempt.CompletedAt:u}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(step.AttemptsText))
        {
            AttemptSummaryChip = step.AttemptsText;
        }
        else
        {
            AttemptSummaryChip = string.Empty;
        }

        ConditionSummary = conditionSummary ?? string.Empty;
    }

    /// Refreshes only the volatile chip/primary line while a run streams updates for the shown step.
    public void SyncLive(HierarchyStepViewModel step)
    {
        DetailChipText = StatusChip.FromStatus(step.StatusText, step.Verdict);
        DetailPrimaryLine = !string.IsNullOrWhiteSpace(step.KeyValue)
            ? step.KeyValue!
            : step.StatusText;
    }

    /// Appends up to maxToAdd detail lines, dropping oldest when over DetailCap. Returns how many were added.
    public int AppendDetailLines(IReadOnlyList<string> lines, int maxToAdd)
    {
        var added = 0;
        foreach (var line in lines)
        {
            if (added >= maxToAdd)
            {
                break;
            }

            if (DetailLines.Count >= DetailCap)
            {
                DetailLines.RemoveAt(0);
            }

            DetailLines.Add(line);
            added++;
        }

        return added;
    }
}
