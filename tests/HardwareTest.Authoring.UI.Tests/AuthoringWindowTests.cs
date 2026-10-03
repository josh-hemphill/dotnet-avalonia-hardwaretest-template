using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class AuthoringWindowTests
{
    [AvaloniaFact]
    public void Empty_session_shows_welcome_action_and_hides_editor()
    {
        using var fixture = new AuthoringUiFixture();
        var window = fixture.Show();

        Assert.True(fixture.Control<Button>("Open workspace from welcome").IsEffectivelyVisible);
        Assert.False(window.FindControl<TabControl>("WorkspaceTabs")!.IsEffectivelyVisible);
        Assert.False(fixture.Control<Button>("Save plan").IsEnabled);
        Assert.False(fixture.ViewModel.HasWorkspace);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void Remembered_sample_workspace_renders_and_bindings_settle_without_dirtying_the_plan()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();

        var programs = fixture.Control<ListBox>("Programs");
        Assert.Equal("sample", fixture.ViewModel.SelectedProgram!.PlanId);
        Assert.Same(fixture.ViewModel.SelectedProgram, programs.SelectedItem);
        Assert.Contains(programs.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "Sample Hardware Suite (Demo)");
        Assert.True(window.FindControl<TabControl>("WorkspaceTabs")!.IsEffectivelyVisible);
        Assert.NotEmpty(fixture.Control<ListBox>("Program sequence").Items);
        Assert.True(fixture.Control<OperatorPreviewPane>("Operator preview chrome").IsEffectivelyVisible);
        Assert.False(fixture.Control<Button>("Open workspace from welcome").IsEffectivelyVisible);

        var notifications = 0;
        fixture.ViewModel.PropertyChanged += (_, _) => notifications++;
        for (var pass = 0; pass < 3; pass++)
        {
            AuthoringUiFixture.Drain();
        }

        Assert.Equal(0, notifications);
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.Null(fixture.ViewModel.Error);
    }

    [AvaloniaFact]
    public void Populated_unit_and_function_choices_keep_selection_identity_through_editor_refresh()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show();
        fixture.OpenRememberedWorkspace();
        var viewModel = fixture.ViewModel;
        var acquire = Assert.Single(viewModel.SequenceItems, row => row.Label == "Acquire VDC");
        fixture.Control<ListBox>("Program sequence").SelectedItem = acquire;
        AuthoringUiFixture.Drain();

        var units = fixture.Control<ComboBox>(AuthoringInspectorCopy.OperatorYUnitLabel);
        var functions = fixture.Control<ComboBox>(AuthoringInspectorCopy.MeasureRecipeLabel);
        Assert.True(units.IsEffectivelyVisible);
        Assert.True(functions.IsEffectivelyVisible);
        Assert.NotEmpty(units.Items);
        Assert.NotEmpty(functions.Items);
        Assert.Equal("V", units.SelectedItem);
        Assert.Same(Assert.Single(units.Items.OfType<string>(), unit => unit == viewModel.YUnit), units.SelectedItem);
        Assert.Same(viewModel.YUnitOptions, units.ItemsSource);
        Assert.Same(viewModel.MetricFunctionChoices, functions.ItemsSource);
        Assert.Same(viewModel.SelectedMetricFunction, functions.SelectedItem);
        Assert.Same(Assert.Single(functions.Items.OfType<AuthoringFunctionDisplay>(),
            choice => choice.Id == viewModel.MetricFunctionId), functions.SelectedItem);

        var program = viewModel.SelectedProgram;
        var unitChoices = units.ItemsSource;
        var functionChoices = functions.ItemsSource;
        var unitSelection = units.SelectedItem;
        var functionSelection = functions.SelectedItem;
        // Re-selecting the current row refreshes all editor bindings without making an edit.
        viewModel.SelectSequence(viewModel.SelectedSequenceIndex);
        AuthoringUiFixture.Drain();

        Assert.Same(unitChoices, units.ItemsSource);
        Assert.Same(functionChoices, functions.ItemsSource);
        Assert.Same(unitSelection, units.SelectedItem);
        Assert.Same(functionSelection, functions.SelectedItem);
        Assert.Same(viewModel.SelectedMetricFunction, functions.SelectedItem);
        Assert.Same(program, viewModel.SelectedProgram);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Null(viewModel.Error);
    }

    [AvaloniaFact]
    public void Typing_program_fields_updates_draft_and_unsaved_indicator_and_sidecar_save_clears_it()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show();
        fixture.OpenRememberedWorkspace();
        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();

        fixture.Type(fixture.Control<TextBox>("Display name"), "UI edited sample");
        fixture.Type(fixture.Control<TextBox>("DUT family"), "ui-fixture");

        Assert.Equal("UI edited sample", fixture.ViewModel.DisplayName);
        Assert.Equal("ui-fixture", fixture.ViewModel.SelectedProgram!.Sidecar.DutFamily);
        Assert.True(fixture.ViewModel.HasUnsavedChanges);
        Assert.True(fixture.Control<TextBlock>("Unsaved changes").IsEffectivelyVisible);

        AuthoringUiFixture.Click(fixture.Control<Button>("Save sidecar"));
        Assert.False(fixture.ViewModel.HasUnsavedChanges);
        Assert.False(fixture.Control<TextBlock>("Unsaved changes").IsEffectivelyVisible);
        Assert.Equal("Saved sample.program.json", fixture.Control<TextBlock>("Authoring status").Text);
        Assert.Null(fixture.ViewModel.Error);
        var reloaded = new AuthoringWorkspaceViewModel(preferences: fixture.Preferences);
        reloaded.Open(fixture.WorkspaceRoot);
        Assert.Equal("UI edited sample", reloaded.DisplayName);
        Assert.Equal("ui-fixture", reloaded.DutFamily);
    }

    [AvaloniaFact]
    public void Independent_sessions_keep_preferences_and_last_workspace_in_their_own_roots()
    {
        using var first = new AuthoringUiFixture(rememberWorkspace: true);
        using var second = new AuthoringUiFixture();
        var firstWindow = first.Show();
        second.Show();
        first.OpenRememberedWorkspace();

        AuthoringUiFixture.Click(first.Control<Button>("Open settings"));
        var settings = Assert.Single(firstWindow.OwnedWindows);
        var rawXml = Assert.Single(settings.GetVisualDescendants().OfType<CheckBox>(),
            box => Equals(box.Content, "Show raw step XML"));
        var initial = rawXml.IsChecked;
        Assert.True(rawXml.Focus());
        settings.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        settings.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        AuthoringUiFixture.Drain();

        first.Preferences.Load();
        second.Preferences.Load();
        Assert.Equal(first.WorkspaceRoot, first.Preferences.Current.LastWorkspace);
        Assert.Null(second.Preferences.Current.LastWorkspace);
        Assert.NotEqual(first.Preferences.FilePath, second.Preferences.FilePath);
        Assert.NotEqual(initial, first.Preferences.Current.ShowRawStepXml);
        Assert.Equal(initial, second.Preferences.Current.ShowRawStepXml);
        Assert.False(second.ViewModel.HasWorkspace);
    }

    [AvaloniaTheory]
    [InlineData(960, 600)]
    [InlineData(1280, 800)]
    public void Supported_window_sizes_load_workspace_and_focus_editable_fields(int width, int height)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        var window = fixture.Show(width, height);
        fixture.OpenRememberedWorkspace();
        Assert.Equal(new Size(width, height), window.ClientSize);
        AssertInsideWindow(fixture.Control<Button>("Save plan"), window);
        AssertInsideWindow(fixture.Control<ListBox>("Programs"), window);

        window.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 1;
        AuthoringUiFixture.Drain();
        var displayName = fixture.Control<TextBox>("Display name");
        displayName.BringIntoView();
        AuthoringUiFixture.Drain();
        AssertInsideWindow(displayName, window);
        Assert.True(displayName.Focus());
        AuthoringUiFixture.Drain();
        Assert.True(displayName.IsFocused);
        Assert.Null(fixture.ViewModel.Error);
    }

    private static void AssertInsideWindow(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        var origin = control.TranslatePoint(default, window);
        Assert.NotNull(origin);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(origin.Value, control.Bounds.Size)),
            $"{control.GetType().Name} at {origin} ({control.Bounds.Size}) must fit inside {window.ClientSize}.");
    }
}
