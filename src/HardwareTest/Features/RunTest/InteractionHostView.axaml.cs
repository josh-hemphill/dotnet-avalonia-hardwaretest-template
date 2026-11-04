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
        AttachedToVisualTree += (_, _) => OnDataContextChanged(this, EventArgs.Empty);
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        SizeChanged += (_, _) => KeepFocusedFieldVisible();
        PromptBodyScroller.SizeChanged += (_, _) => KeepFocusedFieldVisible();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == FontSizeProperty) KeepFocusedFieldVisible();
        };
    }

    private bool _focusLayoutQueued;

    private void KeepFocusedFieldVisible()
    {
        if (_focusLayoutQueued) return;
        _focusLayoutQueued = true;
        var owner = _subscribed;
        Dispatcher.UIThread.Post(() =>
        {
            _focusLayoutQueued = false;
            if (owner is null || !ReferenceEquals(owner, _subscribed) || !IsEffectivelyVisible) return;
            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            if (focused?.DataContext is InteractionFieldViewModel
                && focused.GetVisualAncestors().Contains(this)) focused.BringIntoView();
        }, DispatcherPriority.Loaded);
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
        vm.Interaction.RequestFocusField += OnRequestFocusField;
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
        _subscribed.Interaction.RequestFocusField -= OnRequestFocusField;
        _subscribed = null;
    }

    private void OnRequestFocusFirstField(object? sender, EventArgs e) => ScheduleFocusFirstField();

    private void OnRequestFocusField(string id)
    {
        var owner = _subscribed;
        Dispatcher.UIThread.Post(() =>
        {
            if (owner is null || !ReferenceEquals(owner, _subscribed)) return;
            var field = this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                control.IsEffectivelyVisible && control.IsEffectivelyEnabled
                && control.DataContext is InteractionFieldViewModel model && model.Id == id
                && control is TextBox or CheckBox);
            field?.BringIntoView();
            field?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ScheduleFocusFirstField()
    {
        void FocusFirst()
        {
            var box = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(t => t.IsVisible && t.IsEnabled);
            if (box is not null)
            {
                box.BringIntoView();
                box.Focus();
                return;
            }

            ContinueButton.Focus();
        }

        Dispatcher.UIThread.Post(FocusFirst, DispatcherPriority.Loaded);
    }

    private void OnInteractionFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _subscribed is null)
        {
            return;
        }

        var continueCommand = (ICommand)_subscribed.ContinueOperatorCommand;
        if (!continueCommand.CanExecute(null))
        {
            return;
        }

        continueCommand.Execute(null);
        e.Handled = true;
    }
}
