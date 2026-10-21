using System;
using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ClearRunCommand { get; private set; } = null!;

    public bool CanClearRun => !IsRunning
        && !_runControl.IsRunning
        && !_runControl.IsSafetyStopping
        && !Interaction.IsAwaitingOperator
        && !_runSession.IsAwaitingOperator
        && !ProgramSelection.IsBusy;

    private IObservable<bool> ObserveCanClearRun()
        => Observable.Create<bool>(observer =>
        {
            INotifyPropertyChanged[] sources = [this, _runControl, _runSession, Interaction, ProgramSelection];
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (!string.IsNullOrEmpty(args.PropertyName)
                    && args.PropertyName is not (nameof(IsRunning)
                        or nameof(ProgramSelectionViewModel.IsBusy)
                        or nameof(InteractionHostViewModel.IsAwaitingOperator)
                        or nameof(HardwareTest.Core.Engine.IRunControl.IsSafetyStopping)))
                {
                    return;
                }

                this.RaisePropertyChanged(nameof(CanClearRun));
                observer.OnNext(CanClearRun);
            }

            foreach (var source in sources)
            {
                source.PropertyChanged += Changed;
            }

            observer.OnNext(CanClearRun);
            return () =>
            {
                foreach (var source in sources)
                {
                    source.PropertyChanged -= Changed;
                }
            };
        }).DistinctUntilChanged();

    /// Clears the working display, keeping the confirmed DUT, program and saved Results.
    private void ClearRun()
    {
        // Also guard execution so callers cannot clear an active or paused run.
        if (!CanClearRun)
        {
            return;
        }

        ResetPumpForRun();
        ClearSessionAttempts();
        StepTree.ClearRunState();
        Live.ResetForRun();
        StepDetail.DetailLines.Clear();
        StepDetail.DetailKeyValues.Clear();
        StepDetail.AttemptSummaryChip = string.Empty;
        StepDetail.DetailPrimaryLine = string.Empty;
        StepDetail.DetailChipText = "Pending";
        Interaction.Clear();
        CurrentStepName = string.Empty;
        CurrentStepPath = string.Empty;
        IterationText = string.Empty;
        OverallPercent = 0;
        LastRunId = null;
        ClearLocalRunBanner();
        ClearRunBannerFromShell();
        Workspace.ResetToSteps();
        OpenSelectedDetail();
        Status = "Run display cleared. Saved runs remain in Results.";
        RefreshHero();
    }
}
