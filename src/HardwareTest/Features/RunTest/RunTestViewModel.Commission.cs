using System;
using System.Collections.Generic;
using HardwareTest.Features.Shell;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    private StationBindRequestedEventArgs? _pendingBindRequest;

    public event EventHandler<StationBindRequestedEventArgs>? NavigateToInstrumentsRequested;

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenInstrumentsFromBannerCommand { get; private set; } = null!;

    private void InitCommissioningCommands()
    {
        OpenInstrumentsFromBannerCommand = ReactiveCommand.Create(RaisePendingBindNavigation);
    }

    /// Run gate: persist the bind request, offer Open Instruments on the shell strip, and deep-link.
    private void OnStationNotReady(string planId, IReadOnlyList<string> slotNames)
    {
        _pendingBindRequest = new StationBindRequestedEventArgs
        {
            PlanId = planId,
            SlotNames = slotNames,
        };

        if (_shellNotification is not null && !string.IsNullOrWhiteSpace(BannerMessage))
        {
            _shellNotification.Publish(
                ShellNotificationBrushConverter.FromRun(BannerSeverity),
                BannerMessage,
                dismissible: true,
                sourceKey: ShellNotificationViewModel.SourceRun,
                primary: new ShellNotificationAction
                {
                    Label = "Open Instruments",
                    Command = OpenInstrumentsFromBannerCommand,
                },
                onDismissed: ClearLocalRunBanner);
        }

        RaisePendingBindNavigation();
    }

    private void RaisePendingBindNavigation()
    {
        if (_pendingBindRequest is null)
        {
            return;
        }

        NavigateToInstrumentsRequested?.Invoke(this, _pendingBindRequest);
    }
}
