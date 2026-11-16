using Avalonia.Controls;
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

    private void OnResolveEditingIssue(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AuthoringEditingIssue issue }
            || DataContext is not AuthoringWorkspaceViewModel vm
            || TopLevel.GetTopLevel(this) is not MainWindow { IsVisible: true } owner
            || !ReferenceEquals(owner.DataContext, vm)) return;
        if (issue.RecoveryArea == AuthoringRecoveryArea.CurrentLocation)
        {
            owner.OnOpenFindingProgram(sender, e);
            return;
        }
        // Use the same current-issue and plan-ownership checks as ordinary finding
        // navigation before opening a recovery task. A stale card cannot redirect it.
        if (vm.NavigateEditingIssue(issue) is null) return;
        var route = issue.RecoveryArea switch
        {
            AuthoringRecoveryArea.Environment => "3",
            AuthoringRecoveryArea.Instruments => "1",
            _ => "0"
        };
        owner.OnNavigateTask(new Button { Tag = route }, e);
    }
}
