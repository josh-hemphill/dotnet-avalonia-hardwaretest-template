using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class OperationDetailsTests
{
    [AvaloniaFact]
    public void Full_error_can_be_selected_and_escape_returns_to_details_trigger()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = 20;
        fixture.OpenRememberedWorkspace();
        var message = string.Join("\n", Enumerable.Range(0, 30).Select(index => $"Finding {index}: check the package requirement and try again."));
        fixture.ViewModel.ReportError(message);
        var revision = fixture.ViewModel.SelectedDocument!.Revision;
        var trigger = fixture.Control<Button>("View operation details");
        Assert.True(trigger.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        var dialog = Assert.Single(window.OwnedWindows);
        var details = fixture.Control<TextBox>("Full operation details", dialog);
        Assert.Contains(message, details.Text);
        Assert.True(details.IsReadOnly);
        details.SelectAll();
        Assert.Contains(message, details.SelectedText);
        ResponsiveShellTests.Inside(details, dialog);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Close operation details", dialog), dialog);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.True(trigger.IsFocused);
        Assert.Equal(revision, fixture.ViewModel.SelectedDocument.Revision);
    }
}
