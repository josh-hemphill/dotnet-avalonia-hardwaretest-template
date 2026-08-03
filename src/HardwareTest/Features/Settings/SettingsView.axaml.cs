using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace HardwareTest.Features.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
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
    }
}
