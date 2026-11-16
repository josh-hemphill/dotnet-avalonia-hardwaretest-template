using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features;

namespace HardwareTest.Features.Settings;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _offlineBundleOwner;
    private long _offlineBundleFocusActivity;
    private bool _offlineBundleOwnsLastInteraction;

    public SettingsView()
    {
        InitializeComponent();
        AddHandler(InputElement.GotFocusEvent, (_, args) => TrackOfflineBundleInteraction(args), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(InputElement.PointerPressedEvent, (_, args) => TrackOfflineBundleInteraction(args), RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            if (_offlineBundleOwner is not { } vm) return;
            vm.OfflineBundleFocusRequested -= FocusOfflineField;
            vm.OfflineBundleFocusRequested += FocusOfflineField;
            vm.OfflineBundleInspectionInvalidating -= RecoverInvalidatedInspection;
            vm.OfflineBundleInspectionInvalidating += RecoverInvalidatedInspection;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_offlineBundleOwner is not { } vm) return;
            vm.OfflineBundleFocusRequested -= FocusOfflineField;
            vm.OfflineBundleInspectionInvalidating -= RecoverInvalidatedInspection;
            _offlineBundleFocusActivity++;
            _offlineBundleOwnsLastInteraction = false;
            vm.StopOfflineBundleInspection();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_offlineBundleOwner is { } previous)
        {
            previous.OfflineBundleFocusRequested -= FocusOfflineField;
            previous.OfflineBundleInspectionInvalidating -= RecoverInvalidatedInspection;
            previous.StopOfflineBundleInspection();
        }
        _offlineBundleFocusActivity++;
        _offlineBundleOwnsLastInteraction = false;
        _offlineBundleOwner = DataContext as SettingsViewModel;
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        vm.CopyTextAsync = async text =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                throw new InvalidOperationException("Clipboard is unavailable.");
            }

            await clipboard.SetTextAsync(text);
        };
        vm.OfflineBundleFocusRequested += FocusOfflineField;
        vm.OfflineBundleInspectionInvalidating += RecoverInvalidatedInspection;
    }

    private void OnOpenRun(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel shell)
            shell.NavigateToPageId("RunTest");
    }

    private async void OnChooseOfflineBundleFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || !vm.OfflineBundleCanEdit || TopLevel.GetTopLevel(this) is not { } owner) return;
        var generation = vm.OfflineBundleGeneration;
        try
        {
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose offline bench bundle folder", AllowMultiple = false });
            if (!ReferenceEquals(DataContext, vm) || TopLevel.GetTopLevel(this) != owner || generation != vm.OfflineBundleGeneration) return;
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) vm.OfflineBundlePath = path;
        }
        catch (Exception)
        {
            if (ReferenceEquals(DataContext, vm) && TopLevel.GetTopLevel(this) == owner && generation == vm.OfflineBundleGeneration)
                vm.OfflineBundleFolderPickerUnavailable();
        }
    }

    private void OnOfflineFieldFocused(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Control control) Dispatcher.UIThread.Post(() => control.BringIntoView(), DispatcherPriority.Loaded);
    }

    private bool IsWithinOfflineInspection(object? source)
    {
        var panel = this.FindControl<StackPanel>("OfflineBundleInspectionPanel");
        return source is Control control && panel is not null
            && (ReferenceEquals(control, panel) || control.GetVisualAncestors().Contains(panel));
    }

    private void TrackOfflineBundleInteraction(RoutedEventArgs args)
    {
        _offlineBundleFocusActivity++;
        _offlineBundleOwnsLastInteraction = IsWithinOfflineInspection(args.Source);
    }

    private void RecoverInvalidatedInspection()
    {
        if (!_offlineBundleOwnsLastInteraction || DataContext is not SettingsViewModel vm || TopLevel.GetTopLevel(this) is not { } owner) return;
        var focused = owner.FocusManager?.GetFocusedElement();
        if (focused is not null && !IsWithinOfflineInspection(focused)) return;
        var activity = _offlineBundleFocusActivity;
        var generation = vm.OfflineBundleGeneration;
        FocusOfflineField("Offline bundle folder", () =>
        {
            var current = owner.FocusManager?.GetFocusedElement();
            return activity == _offlineBundleFocusActivity && generation == vm.OfflineBundleGeneration
                && (current is null || IsWithinOfflineInspection(current));
        });
    }

    private void FocusOfflineField(string name) => FocusOfflineField(name, null);

    private void FocusOfflineField(string name, Func<bool>? stillOwnsFocus)
    {
        var context = DataContext;
        var owner = TopLevel.GetTopLevel(this);
        var field = name switch
        {
            "New operator copy folder" => this.FindControl<TextBox>("OfflineBundleDestinationBox"),
            "Fresh operator data folder" => this.FindControl<TextBox>("OfflineBundleDataBox"),
            _ => this.FindControl<TextBox>("OfflineBundlePathBox"),
        };
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, context) || owner is null || TopLevel.GetTopLevel(this) != owner || stillOwnsFocus?.Invoke() == false) return;
            field?.BringIntoView();
            (name == "Offline bundle folder" ? this.FindControl<TextBlock>("OfflineBundleStatusText") : this.FindControl<TextBlock>("OfflineBundleCommandStatusText"))?.BringIntoView();
            field?.Focus();
        }, DispatcherPriority.Loaded);
    }

    private async void OnOfflineBundleDetails(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { HasOfflineBundleInspection: true } vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        var text = new TextBox
        {
            Text = vm.OfflineBundleDetails,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        Avalonia.Automation.AutomationProperties.SetName(text, "Full offline bundle inspection");
        var close = new Button { Content = "Close", MinHeight = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Avalonia.Thickness(0, 12, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(close, "Close offline bundle inspection");
        DockPanel.SetDock(close, Dock.Bottom);
        var body = new DockPanel { Margin = new Avalonia.Thickness(16), LastChildFill = true };
        body.Children.Add(close); body.Children.Add(text);
        var dialog = new Window
        {
            Title = "Offline bench bundle inspection",
            Content = body,
            FontSize = owner.FontSize,
            RequestedThemeVariant = owner.ActualThemeVariant,
            Width = Math.Min(760, Math.Max(360, owner.Bounds.Width - 40)),
            Height = Math.Min(600, Math.Max(300, owner.Bounds.Height - 40)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        close.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(); } };
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() => text.Focus(), DispatcherPriority.Loaded);
        void InspectionChanged(object? source, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SettingsViewModel.HasOfflineBundleInspection) && !vm.HasOfflineBundleInspection)
                dialog.Close();
        }
        vm.PropertyChanged += InspectionChanged;
        try { await dialog.ShowDialog(owner); }
        finally { vm.PropertyChanged -= InspectionChanged; }
        if (!ReferenceEquals(DataContext, vm) || TopLevel.GetTopLevel(this) != owner) return;
        if (vm.HasOfflineBundleInspection) (sender as Control)?.Focus();
        else FocusOfflineField("Offline bundle folder");
    }
}
