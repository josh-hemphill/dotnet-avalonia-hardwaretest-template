using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class RunPreparationView : UserControl
{
    private RunTestViewModel? _subscribed;

    public RunPreparationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => OnDataContextChanged(this, EventArgs.Empty);
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is not RunTestViewModel vm)
        {
            return;
        }

        _subscribed = vm;
        vm.SessionPanel.RequestFocusDutSerial += OnRequestFocusDutSerial;
        vm.SessionPanel.RequestFocusSessionField += OnRequestFocusSessionField;
        vm.SessionPanel.RequestFocusRun += OnRequestFocusRun;
        if (vm.SessionPanel.NeedsDutConfirm)
        {
            ScheduleFocusDutSerial();
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.SessionPanel.RequestFocusDutSerial -= OnRequestFocusDutSerial;
        _subscribed.SessionPanel.RequestFocusSessionField -= OnRequestFocusSessionField;
        _subscribed.SessionPanel.RequestFocusRun -= OnRequestFocusRun;
        _subscribed = null;
    }

    private void OnRequestFocusDutSerial(object? sender, EventArgs e) => ScheduleFocusDutSerial();

    private void ScheduleFocusDutSerial()
    {
        OnRequestFocusSessionField(OperatorSessionPanelViewModel.SessionFieldDutSerial);
    }

    private void OnRequestFocusSessionField(string field)
    {
        var owner = _subscribed;
        Dispatcher.UIThread.Post(() =>
        {
            if (owner is not null && ReferenceEquals(owner, _subscribed)) FocusSessionField(field);
        }, DispatcherPriority.Loaded);
    }

    private void OnRequestFocusRun(object? sender, EventArgs e)
    {
        var owner = _subscribed;
        var board = this.GetVisualAncestors().OfType<RunTestView>().FirstOrDefault();
        Dispatcher.UIThread.Post(() =>
        {
            if (owner is null || !ReferenceEquals(owner, _subscribed) || board?.IsEffectivelyVisible != true) return;
            var run = board.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
                button.IsEffectivelyVisible && button.IsEffectivelyEnabled && AutomationProperties.GetName(button) == "Run");
            run?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void OnSessionFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _subscribed is null)
        {
            return;
        }

        var next = _subscribed.SessionPanel.NextIncompleteSessionField();
        var current = SessionFieldId(sender);
        if (!string.IsNullOrEmpty(next)
            && !string.Equals(next, current, StringComparison.Ordinal)
            && FocusSessionField(next))
        {
            e.Handled = true;
            return;
        }

        ((ICommand)_subscribed.SessionPanel.ConfirmSessionCommand).Execute(null);
        e.Handled = true;
    }

    private static string? SessionFieldId(object? sender)
        => sender is Control control
            ? control.Name switch
            {
                nameof(DutSerialBox) => OperatorSessionPanelViewModel.SessionFieldDutSerial,
                nameof(DutPartBox) => OperatorSessionPanelViewModel.SessionFieldDutPart,
                nameof(DutRevisionBox) => OperatorSessionPanelViewModel.SessionFieldDutRevision,
                nameof(TechnicianBox) => OperatorSessionPanelViewModel.SessionFieldTechnician,
                _ => control.Name,
            }
            : null;

    private bool FocusSessionField(string field)
    {
        Control? target = field switch
        {
            OperatorSessionPanelViewModel.SessionFieldDutSerial => DutSerialBox,
            OperatorSessionPanelViewModel.SessionFieldDutPart => DutPartBox,
            OperatorSessionPanelViewModel.SessionFieldDutRevision => DutRevisionBox,
            OperatorSessionPanelViewModel.SessionFieldTechnician => TechnicianBox.IsEffectivelyVisible ? TechnicianBox : StaleTechnicianBox,
            OperatorSessionPanelViewModel.SessionFieldCredential => CredentialCaptureButton.IsEffectivelyVisible ? CredentialCaptureButton : StaleCredentialCaptureButton,
            _ => null,
        };
        if (target is null || !target.IsEffectivelyVisible || !target.IsEffectivelyEnabled)
        {
            return false;
        }

        target.BringIntoView();
        return target.Focus();
    }

    private void OnStaleTechnicianKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _subscribed is null)
        {
            return;
        }

        ((ICommand)_subscribed.SessionPanel.ConfirmSameDutCommand).Execute(null);
        e.Handled = true;
    }

    private void OnTechnicianGotFocus(object? sender, RoutedEventArgs e)
        => _subscribed?.SessionPanel.OnTechnicianFocused();
}
