using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HardwareTest.Authoring;

public partial class WorkspaceIssuesView : UserControl
{
    public WorkspaceIssuesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookFindings();
        AttachedToVisualTree += (_, _) => HookFindings();
        DetachedFromVisualTree += (_, _) =>
        {
            if (_findingsSession is not null) _findingsSession.PropertyChanged -= OnFindingsChanged;
            _findingsSession = null;
        };
        LayoutUpdated += (_, _) =>
        {
            // The entire task can scroll when editing is expanded. Keep checked rows
            // in a bounded list that can itself fit the minimum-size route viewport.
            var viewport = this.FindControl<ScrollViewer>("TaskViewport")!;
            var list = this.FindControl<ListBox>("CheckedFindingsList")!;
            if (list.TranslatePoint(default, viewport) is not { } position) return;
            var contentTop = position.Y + viewport.Offset.Y;
            var height = Math.Clamp(viewport.Viewport.Height - contentTop - 8, 96, 320);
            if (Math.Abs(list.Height - height) > 0.5) list.Height = height;
        };
    }

    private AuthoringWorkspaceViewModel? _findingsSession;
    private Guid? _lastWorkspace;
    private int _lastEditingCount;
    private bool _wasEditingOnly;

    private void HookFindings()
    {
        if (_findingsSession is not null) _findingsSession.PropertyChanged -= OnFindingsChanged;
        _findingsSession = DataContext as AuthoringWorkspaceViewModel;
        if (_findingsSession is not null) _findingsSession.PropertyChanged += OnFindingsChanged;
        RevealEditingFindings();
    }

    private void OnFindingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(AuthoringWorkspaceViewModel.EditingIssues)
            or nameof(AuthoringWorkspaceViewModel.FindingRows) or nameof(AuthoringWorkspaceViewModel.WorkspaceSessionId))
            RevealEditingFindings();
    }

    private void RevealEditingFindings()
    {
        if (_findingsSession is not { } vm) return;
        var editingOnly = vm.EditingIssues.Count > 0 && vm.FindingRows.Count == 0;
        if (_lastWorkspace != vm.WorkspaceSessionId)
            EditingFindingsSection.IsExpanded = editingOnly;
        else if (editingOnly && (!_wasEditingOnly || _lastEditingCount != vm.EditingIssues.Count))
            EditingFindingsSection.IsExpanded = true;
        _lastWorkspace = vm.WorkspaceSessionId;
        _lastEditingCount = vm.EditingIssues.Count;
        _wasEditingOnly = editingOnly;
    }

    private void OnOpenFindingProgram(object? sender, RoutedEventArgs e)
        => (TopLevel.GetTopLevel(this) as MainWindow)?.OnOpenFindingProgram(sender, e);
}
