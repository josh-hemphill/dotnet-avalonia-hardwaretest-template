using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class BenchReferenceView : UserControl
{
    private BenchReferencePanelViewModel? _observed;
    private bool _attached;
    public BenchReferenceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe();
        AttachedToVisualTree += (_, _) => { _attached = true; Observe(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Unsubscribe(); };
    }
    private void Observe()
    {
        if (ReferenceEquals(_observed, DataContext)) return;
        Unsubscribe(); _observed = DataContext as BenchReferencePanelViewModel;
        if (_observed is not null) _observed.RequestFocusField += FocusField;
    }
    private void Unsubscribe()
    {
        if (_observed is not null) _observed.RequestFocusField -= FocusField;
        _observed = null;
    }
    private void FocusField(string field)
    {
        var expected = _observed;
        Dispatcher.Post(() =>
        {
            if (!_attached || !ReferenceEquals(expected, _observed)) return;
            var input = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(control => AutomationProperties.GetName(control) == field);
            if (input is null) return;
            var group = input.Parent as Control ?? input;
            group.BringIntoView(); input.Focus(); group.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
