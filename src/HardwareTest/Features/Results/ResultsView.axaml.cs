using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;

namespace HardwareTest.Features.Results;

public partial class ResultsView : UserControl
{
    private ResultsViewModel? _observed;
    private bool _arranging;
    private bool _showingDetail;
    private bool _openingRun;
    private string? _closedRunId;
    private Control? _signingReturnFocus;

    public ResultsView()
    {
        InitializeComponent();
        // ListBoxItem handles Enter itself. Intercept in the tunnel so the row
        // that naturally regains focus after Close can reopen its saved run.
        RunResultsList.AddHandler(KeyDownEvent, OnRunsKeyDown, RoutingStrategies.Tunnel);
        RunIdentityButton.Flyout!.Opened += (_, _) =>
        {
            RunIdentityBox.FontSize = FontSize;
            RunIdentityBox.Focus();
        };
        RunIdentityButton.Flyout.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (RunIdentityButton.IsEffectivelyVisible && RunIdentityButton.IsEffectivelyEnabled
                && _observed?.ShowAttestationPrompt != true) RunIdentityButton.Focus();
        }, DispatcherPriority.Loaded);
        DataContextChanged += (_, _) => ObserveViewModel();
        AttachedToVisualTree += (_, _) => ObserveViewModel();
        DetachedFromVisualTree += (_, _) => StopObserving();
        SizeChanged += (_, _) => ArrangeResults();
        PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) ArrangeResults(); };
    }

    private void ObserveViewModel()
    {
        StopObserving();
        _observed = DataContext as ResultsViewModel;
        _showingDetail = _observed?.ShowDetail == true;
        _closedRunId = _showingDetail ? null : _observed?.SelectedRun?.RunId;
        if (_observed is not null) _observed.PropertyChanged += OnResultsChanged;
        ArrangeResults();
        if (_observed?.ShowAttestationPrompt == true) FocusSigningPrompt();
    }

    private void StopObserving()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnResultsChanged;
        _observed = null;
    }

    private void OnResultsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsViewModel.ShowAttestationPrompt) or nameof(ResultsViewModel.ShowAttestationPin))
        {
            FocusSigningPrompt();
            return;
        }
        if (e.PropertyName != nameof(ResultsViewModel.ShowDetail)) return;
        var vm = _observed;
        var detail = vm?.ShowDetail == true;
        var wasShowing = _showingDetail;
        _showingDetail = detail;
        if (wasShowing && !detail) _closedRunId = vm?.SelectedRun?.RunId;
        ArrangeResults();
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(vm, _observed) || !IsEffectivelyVisible || vm?.ShowDetail != detail) return;
            if (detail)
                ResultsCloseButton.Focus();
            else if (wasShowing)
            {
                Control? row = null;
                if (RunResultsList.SelectedItem is { } selected)
                {
                    RunResultsList.ScrollIntoView(selected);
                    row = RunResultsList.ContainerFromItem(selected);
                }
                if (row?.Focus() != true) RunResultsList.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void FocusSigningPrompt()
    {
        var vm = _observed;
        if (vm?.ShowAttestationPrompt == true && ResultsLayout.IsEnabled)
        {
            _signingReturnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
            ResultsLayout.IsEnabled = false;
        }
        else if (vm?.ShowAttestationPrompt != true) ResultsLayout.IsEnabled = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(vm, _observed) || !IsEffectivelyVisible) return;
            if (vm?.ShowAttestationPrompt == true)
            {
                if (vm.ShowAttestationPin) AttestationPinBox.Focus();
                else AttestationCaptureButton.Focus();
            }
            else
            {
                var previous = _signingReturnFocus;
                _signingReturnFocus = null;
                if (previous?.IsEffectivelyVisible == true && previous.IsEffectivelyEnabled)
                    previous.Focus();
                else if (ResultsExportButton.IsEffectivelyVisible) ResultsExportButton.Focus();
                else ResultsCloseButton.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ArrangeResults()
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            var wide = Bounds.Width >= (FontSize > 16 ? 1100 : 860);
            var detail = _observed?.ShowDetail == true;
            ResultsLayout.ColumnDefinitions[1].Width = wide && detail ? new GridLength(400) : new GridLength(0);
            Grid.SetColumn(ResultsDetailPane, wide ? 1 : 0);
            ResultsListPane.IsVisible = wide || !detail;
            var caption = wide ? "Close details" : "Back to results";
            if (!Equals(ResultsCloseButton.Content, caption)) ResultsCloseButton.Content = caption;
            ResultsNavigationHint.Text = detail && !wide
                ? "Back to results returns to the selected run."
                : "Select a run to view its details. Double-click opens its default report.";
        }
        finally { _arranging = false; }
    }

    private async void OnRunsTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control source) return;
        var row = source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (row?.DataContext is not TestRunSummary summary || _closedRunId != summary.RunId) return;
        await ReopenSelectedRunAsync();
    }

    private async void OnRunsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ResultsViewModel { ShowDetail: false, SelectedRun: not null }) return;
        e.Handled = true;
        await ReopenSelectedRunAsync();
    }

    private void OnResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape) return;
        if (DataContext is ResultsViewModel { ShowAttestationPrompt: true } signing)
        {
            signing.CancelPendingReportAction();
            e.Handled = true;
            return;
        }
        if (DataContext is not ResultsViewModel { ShowDetail: true }) return;
        if (ResultsCloseButton.Command?.CanExecute(null) == true)
        {
            ResultsCloseButton.Command.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRunIdentityKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        RunIdentityButton.Flyout?.Hide();
    }

    private async Task ReopenSelectedRunAsync()
    {
        if (_openingRun || DataContext is not ResultsViewModel { ShowDetail: false, IsBusy: false, SelectedRun: not null } vm) return;
        _openingRun = true;
        try { await vm.OpenSelectedRunAsync(); }
        catch (Exception error) { if (ReferenceEquals(vm, DataContext)) vm.Status = $"Could not open run details: {error.Message}"; }
        finally { _openingRun = false; }
    }

    private async void OnRunsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ResultsViewModel vm)
        {
            try { await vm.OpenSelectedRunDefaultReportAsync(); }
            catch (Exception error) { if (ReferenceEquals(vm, DataContext)) vm.Status = $"Could not open the report: {error.Message}"; }
        }
    }
}
