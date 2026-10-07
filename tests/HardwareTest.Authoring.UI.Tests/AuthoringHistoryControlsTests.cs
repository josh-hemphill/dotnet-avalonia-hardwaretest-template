using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringHistoryControlsTests
{
    [AvaloniaFact]
    public void Program_and_catalog_controls_are_visible_and_restore_their_scoped_content()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var undo = fixture.Control<Button>("Undo selected program");
        var redo = fixture.Control<Button>("Redo selected program");
        Assert.True(undo.IsEffectivelyVisible);
        Assert.False(undo.IsEnabled);
        Assert.False(redo.IsEnabled);
        var original = fixture.ViewModel.DisplayName;
        fixture.ViewModel.DisplayName = "edited";
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(undo);
        Assert.Equal(original, fixture.ViewModel.DisplayName);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        AuthoringUiFixture.Click(redo);
        Assert.Equal("edited", fixture.ViewModel.DisplayName);
        fixture.ViewModel.NewRequiredField = "fixtureId";
        fixture.ViewModel.AddRequiredField();
        AuthoringUiFixture.Drain();
        AuthoringUiFixture.Click(fixture.Control<Button>("Undo workspace catalog"));
        Assert.DoesNotContain("fixtureId", fixture.ViewModel.RequiredFieldOptions);
        AuthoringUiFixture.Click(fixture.Control<Button>("Redo workspace catalog"));
        Assert.Contains("fixtureId", fixture.ViewModel.RequiredFieldOptions);
    }

    [AvaloniaTheory]
    [InlineData("Redo selected program")]
    [InlineData("Undo workspace catalog")]
    [InlineData("Redo workspace catalog")]
    public void History_controls_commit_pending_measure_setting_before_restoring(string control)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        fixture.ViewModel.SelectMeasure(0);
        fixture.ViewModel.SetMetricSetting("TestNote", "initial");
        if (control == "Redo selected program")
        {
            fixture.ViewModel.YUnit = "changed";
            fixture.ViewModel.Undo();
        }
        else
        {
            fixture.ViewModel.NewRequiredField = "fixtureId";
            fixture.ViewModel.AddRequiredField();
            if (control == "Redo workspace catalog") fixture.ViewModel.UndoWorkspace();
        }
        AuthoringUiFixture.Drain();
        var editor = window.GetVisualDescendants().OfType<TextBox>()
            .First(box => box.IsEffectivelyVisible && box.DataContext is AuthoringSettingRow { IsText: true });
        var row = (AuthoringSettingRow)editor.DataContext!;
        editor.BringIntoView();
        AuthoringUiFixture.Drain();
        Assert.True(editor.Focus());
        editor.Text = "7";
        AuthoringUiFixture.Drain();
        var action = fixture.Control<Button>(control);
        if (control == "Redo selected program") AuthoringUiFixture.Click(action);
        else Assert.False(action.IsEnabled); // Opening the menu commits the intervening edit before offering catalog history.
        Assert.Equal("7", fixture.ViewModel.MetricSettingRows.Single(setting => setting.Key == row.Key).Value);
        if (control == "Undo workspace catalog") Assert.Contains("fixtureId", fixture.ViewModel.RequiredFieldOptions);
        if (control == "Redo workspace catalog") Assert.DoesNotContain("fixtureId", fixture.ViewModel.RequiredFieldOptions);
        if (control == "Redo selected program") Assert.False(fixture.ViewModel.CanRedo);
    }
}
