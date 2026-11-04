using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class FocusedHardwareWorkflowTests
{
    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Selected_binding_editor_is_adjacent_and_review_cancel_preserves_the_applied_plan(int fontSize)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(960, 600);
        window.FontSize = fontSize;
        fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        vm.StopRecovery();
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(1);
        var load = fixture.Control<Button>("Load selected binding");
        AuthoringUiFixture.Click(load);
        var type = fixture.Control<ComboBox>("Hardware adapter type");
        var address = fixture.Control<TextBox>("Hardware adapter address");
        var review = fixture.Control<Button>("Review binding change");
        var slots = fixture.Control<ListBox>("Instruments");
        Assert.True(slots.TranslatePoint(default, window)!.Value.X < address.TranslatePoint(default, window)!.Value.X);
        foreach (var control in new Control[] { type, address, review }) ResponsiveShellTests.Inside(control, window);
        Assert.True(address.IsFocused);
        Assert.False(fixture.Control<TextBox>("Hardware adapter timeout").IsEffectivelyEnabled);

        var draft = AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!);
        var applied = vm.SelectedInstrument!.VisaAddress;
        var saved = vm.Workspace!.TapPlanPaths.ToDictionary(path => path, File.ReadAllBytes);
        fixture.Type(address, "MOCK::REVIEWED-ONLY");
        Assert.True(draft.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        Assert.Contains("Unapplied", fixture.Control<TextBlock>("Binding draft state").Text);
        AuthoringUiFixture.Click(review);
        var dialog = Assert.Single(window.OwnedWindows);
        Assert.IsType<HardwareBindingReviewWindow>(dialog);
        Assert.Contains("authoringPalette", dialog.Classes);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Apply reviewed binding", dialog), dialog);
        ResponsiveShellTests.Inside(fixture.Control<Button>("Cancel destructive operation", dialog), dialog);
        Assert.True(fixture.Control<Button>("Cancel destructive operation", dialog).IsFocused);
        Assert.Equal("MOCK::REVIEWED-ONLY", fixture.Control<TextBlock>("Reviewed binding address", dialog).Text);
        Assert.False(fixture.Control<Expander>("Reviewed binding details", dialog).IsExpanded);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(applied, vm.SelectedInstrument.VisaAddress);
        Assert.Equal("MOCK::REVIEWED-ONLY", address.Text);
        Assert.True(draft.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
        foreach (var file in saved) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.True(review.IsFocused);

        AuthoringUiFixture.Click(review);
        dialog = Assert.Single(window.OwnedWindows);
        AuthoringUiFixture.Click(fixture.Control<Button>("Apply reviewed binding", dialog));
        Assert.Equal("MOCK::REVIEWED-ONLY", vm.SelectedInstrument.VisaAddress);
        Assert.Contains("Matches", fixture.Control<TextBlock>("Binding draft state").Text);
        Assert.True(await window.ExecuteCommandAsync("undo"));
        Assert.Equal(applied, vm.SelectedInstrument.VisaAddress);
        Assert.True(draft.ContentEquals(AuthoringDocumentSnapshot.Capture(vm.SelectedProgram!)));
    }

    [AvaloniaFact]
    public async Task Invalid_binding_address_returns_focus_without_changing_the_applied_binding()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        await fixture.WaitForReadinessAsync();
        fixture.NavigateTask(1);
        AuthoringUiFixture.Click(fixture.Control<Button>("Load selected binding"));
        var before = AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!);
        var address = fixture.Control<TextBox>("Hardware adapter address");
        Assert.True(address.Focus());
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(string.Empty, address.Text);
        AuthoringUiFixture.Click(fixture.Control<Button>("Review binding change"));
        Assert.Empty(window.OwnedWindows);
        Assert.Contains("address", fixture.ViewModel.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(address.IsFocused);
        ResponsiveShellTests.Inside(address, window);
        Assert.True(before.ContentEquals(AuthoringDocumentSnapshot.Capture(fixture.ViewModel.SelectedProgram!)));
    }
}
