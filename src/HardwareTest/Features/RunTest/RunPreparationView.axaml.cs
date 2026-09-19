using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HardwareTest.Features.RunTest;

public partial class RunPreparationView : UserControl
{
    private RunTestViewModel? _subscribed;

    public RunPreparationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        _subscribed = null;
    }

    private void OnRequestFocusDutSerial(object? sender, EventArgs e) => ScheduleFocusDutSerial();

    private void ScheduleFocusDutSerial()
    {
        Dispatcher.UIThread.Post(() => DutSerialBox?.Focus(), DispatcherPriority.Loaded);
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
            OperatorSessionPanelViewModel.SessionFieldTechnician => TechnicianBox,
            _ => null,
        };
        if (target is null || !target.IsVisible || !target.IsEnabled)
        {
            return false;
        }

        target.Focus();
        return true;
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
