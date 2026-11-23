using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    /// Continue entry point for the window-level transport bar.
    public void ContinueOperatorAttention() => ContinueOperator();

    internal void RevealWaitingOperatorRequest()
    {
        if (_runSession.PendingInteraction is { } request)
        {
            Interaction.IsAwaitingOperator = true;
            Interaction.Apply(request, fallbackMessage: null);
            _ = PinWaitingStep();
            RefreshHero();
        }
    }

    private bool PinWaitingStep()
    {
        if (!Interaction.IsAwaitingOperator || Interaction.ProducerStepId is not { } id)
        {
            return false;
        }

        var step = StepTree.FindStepById(id.ToString());
        CurrentStepName = step?.Name ?? Interaction.ProducerStepName ?? string.Empty;
        CurrentStepPath = step?.Path ?? string.Empty;
        return true;
    }

    /// Reveals the waiting step under the docked operator prompt.
    private void ShowCurrentStep()
    {
        _ = PinWaitingStep();
        Workspace.OpenSteps();
        StepTree.JumpToCurrent(changeScope: true);
    }

    private void ContinueOperator()
    {
        if (!Interaction.IsAwaitingOperator && !_runSession.IsAwaitingOperator) return;
        var request = _runSession.PendingInteraction;
        if (!Interaction.TryCollectResponse(request, out var values))
        {
            Status = Interaction.InteractionValidationError!;
            _ = PinWaitingStep();
            RefreshHero();
            return;
        }

        var response = request is null
            ? null
            : OperatorInteractionResponse.Continue(request.Id, values);

        ClearPendingOperatorState();
        Interaction.Clear();
        Interaction.IsAwaitingOperator = false;
        Interaction.OperatorPromptMessage = null;
        Status = "Continuing…";
        Session.TouchActivity();
        _runControl.Resume();
        _runSession.Resume(response);
        ScheduleScrollToCurrentStep(changeScope: false);
    }

    private void ScheduleScrollToCurrentStep(bool changeScope = true)
    {
        void Scroll()
        {
            StepTree.JumpToCurrent(changeScope);
            if (StepTree.SelectedStep is null && !string.IsNullOrWhiteSpace(CurrentStepPath))
            {
                return;
            }

            StepTree.RaiseRequestScroll();
        }

        if (UiScheduler is not null)
        {
            UiScheduler(Scroll);
            return;
        }

        Scroll();
    }
}
