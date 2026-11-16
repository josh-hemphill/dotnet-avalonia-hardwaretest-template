using Avalonia.Controls;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

internal static class OperationDetailsUiActions
{
    internal static string ReadAndClose(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Click(fixture.Control<Button>("View operation details"));
        var dialog = Assert.Single(fixture.Window!.OwnedWindows);
        Assert.Equal("Operation details", dialog.Title);
        var details = fixture.Control<TextBox>("Full operation details", dialog);
        Assert.True(details.IsReadOnly);
        Assert.True(details.AcceptsReturn);
        details.SelectAll();
        Assert.Equal(details.Text, details.SelectedText);
        ResponsiveShellTests.Inside(details, dialog);
        var text = details.Text ?? string.Empty;
        AuthoringUiFixture.Click(fixture.Control<Button>("Close operation details", dialog));
        Assert.Empty(fixture.Window.OwnedWindows);
        return text;
    }
}
