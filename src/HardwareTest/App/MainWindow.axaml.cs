using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using HardwareTest.Core.Settings;
using HardwareTest.Features;

namespace HardwareTest;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _settingsStore;
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel, ISettingsStore settingsStore)
    {
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        InitializeComponent();
        DataContext = viewModel;
        RestoreWindowState();

        if (NavView is not null)
        {
            NavView.SelectionChanged += OnNavigationSelectionChanged;
        }

        Closing += OnClosing;
    }

    private void OnNavigationSelectionChanged(object? sender, FluentAvalonia.UI.Controls.FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavItem item)
        {
            _viewModel.NavigateTo(item);
        }
        else if (e.SelectedItem is FluentAvalonia.UI.Controls.FANavigationViewItem nvi
                 && nvi.Tag is string id)
        {
            _viewModel.NavigateToPageId(id);
        }
    }

    private void RestoreWindowState()
    {
        var state = _settingsStore.UiState;
        var width = (int)Math.Max(400, state.NormalWidth);
        var height = (int)Math.Max(300, state.NormalHeight);
        var x = (int)state.NormalX;
        var y = (int)state.NormalY;

        var named = Screens.All
            .Select(s => new MonitorPlacement.ScreenInfo(
                TryGetDeviceName(s),
                s.WorkingArea.X,
                s.WorkingArea.Y,
                s.WorkingArea.Width,
                s.WorkingArea.Height,
                s.IsPrimary))
            .ToArray();

        var target = MonitorPlacement.ResolveScreen(state.MonitorDeviceName, named);
        if (target is { } screen)
        {
            var placement = MonitorPlacement.ClampToScreen(x, y, width, height, screen);
            x = placement.X;
            y = placement.Y;
            width = placement.Width;
            height = placement.Height;
        }

        Position = new PixelPoint(x, y);
        Width = width;
        Height = height;

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var state = _settingsStore.UiState;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is not null)
        {
            state.MonitorDeviceName = TryGetDeviceName(screen);
        }

        if (WindowState == WindowState.Normal)
        {
            state.NormalX = Position.X;
            state.NormalY = Position.Y;
            state.NormalWidth = Width;
            state.NormalHeight = Height;
            state.X = Position.X;
            state.Y = Position.Y;
            state.Width = Width;
            state.Height = Height;
            state.IsMaximized = false;
        }
        else if (WindowState == WindowState.Maximized)
        {
            state.IsMaximized = true;
            state.X = state.NormalX;
            state.Y = state.NormalY;
            state.Width = state.NormalWidth;
            state.Height = state.NormalHeight;
        }

        try
        {
            await _settingsStore.SaveUiStateAsync();
        }
        catch
        {
            // ignore persistence errors on close
        }
    }

    private static string TryGetDeviceName(Screen screen)
    {
        // Avalonia 12 Screen has DisplayName; fall back to working-area identity.
        try
        {
            var name = screen.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }
        catch
        {
            // ignore
        }

        var area = screen.WorkingArea;
        return $"screen@{area.X},{area.Y},{area.Width}x{area.Height}";
    }
}
