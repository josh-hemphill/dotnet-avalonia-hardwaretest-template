using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class InteractionHostView : UserControl
{
    private RunTestViewModel? _subscribed;

    public InteractionHostView()
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
        vm.Interaction.RequestFocusFirstField += OnRequestFocusFirstField;
        if (vm.Interaction.IsAwaitingOperator)
        {
            ScheduleFocusFirstField();
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.Interaction.RequestFocusFirstField -= OnRequestFocusFirstField;
        _subscribed = null;
    }

    private void OnRequestFocusFirstField(object? sender, EventArgs e) => ScheduleFocusFirstField();

    private void ScheduleFocusFirstField()
    {
        void FocusFirst()
        {
            var box = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(t => t.IsVisible && t.IsEnabled);
            box?.Focus();
        }

        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
    }

    private void OnInteractionFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _subscribed is null)
        {
            return;
        }

        ((ICommand)_subscribed.ContinueOperatorCommand).Execute(null);
        e.Handled = true;
    }
}
