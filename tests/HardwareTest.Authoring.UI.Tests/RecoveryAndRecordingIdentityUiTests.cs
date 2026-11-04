using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using Xunit;

namespace HardwareTest.Authoring.UI.Tests;

public sealed class RecoveryAndRecordingIdentityUiTests
{
    [AvaloniaFact]
    public async Task Environment_finding_offers_the_named_recovery_before_complete_technical_details()
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        fixture.Show(); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        var rejectedPath = "invalid\0home";
        vm.OpenTapHomeOverride = rejectedPath;
        await fixture.WaitForReadinessAsync();
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 2;
        AuthoringUiFixture.Drain();
        var issue = Assert.Single(vm.EditingIssues, issue => issue.Code == "INVALID_OPENTAP_HOME");
        var details = fixture.Window!.GetVisualDescendants().OfType<Expander>()
            .Single(control => ReferenceEquals(control.DataContext, issue));
        Assert.False(details.IsExpanded);
        var action = fixture.Window!.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Classes.Contains("authoringAction") && ReferenceEquals(control.DataContext, issue));
        Assert.Equal("Open Environment", action.Content);
        Assert.True(action.IsEffectivelyVisible);
        Assert.DoesNotContain(rejectedPath, issue.RecoverySummary);
        details.IsExpanded = true;
        AuthoringUiFixture.Drain();
        Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == issue.Message);
        Assert.Contains("invalid\\0home", issue.Message);
        var revision = vm.SelectedDocument!.Revision;
        var dirty = vm.HasUnsavedChanges;
        AuthoringUiFixture.Click(action);
        Assert.Equal(3, fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex);
        Assert.Equal(revision, vm.SelectedDocument!.Revision);
        Assert.Equal(dirty, vm.HasUnsavedChanges);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keyboard_recording_choice_shows_complete_identity_and_only_confirmation_changes_the_source(bool confirm)
    {
        using var fixture = new AuthoringUiFixture(rememberWorkspace: true);
        const string commonPrefix = "production-line-bench-2026-11-04-same-prefix-";
        var runIds = new[] { commonPrefix + "first-run", commonPrefix + "second-run" };
        for (var i = 0; i < 2; i++)
        {
            var directory = Path.Combine(fixture.WorkspaceRoot, "recordings", i.ToString());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "run.json"), JsonSerializer.Serialize(new TestRunRecord
            {
                PlanId = "sample",
                RunId = runIds[i],
                DutSerial = $"DEVICE-{i}-full-traceable-identity",
                StartedAt = new DateTimeOffset(2026, 11, 4, 12 + i, 30, 0, TimeSpan.Zero),
                Samples = [new StoredSample { Channel = "VDC", MetricKey = "VDC", Value = i + 1 }]
            }, AppJsonContext.Default.TestRunRecord));
        }
        fixture.Show(width: 960); fixture.OpenRememberedWorkspace();
        var vm = fixture.ViewModel;
        fixture.Window!.FindControl<TabControl>("WorkspaceTabs")!.SelectedIndex = 5;
        vm.SelectDataset(0);
        await fixture.WaitForPreviewAsync();
        Assert.Equal("Recording", vm.PreviewSourceKind);
        Assert.Equal(runIds[0], vm.PreviewSourceName);
        var original = vm.SelectedDataset;
        var revision = vm.SelectedDocument!.Revision;
        var dialog = PreviewUiActions.OpenRecordingPicker(fixture);
        var list = fixture.Control<ListBox>("Recordings", dialog);
        Assert.True(list.Focus());
        dialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        AuthoringUiFixture.Drain();
        Assert.Equal(1, list.SelectedIndex);
        var identity = fixture.Control<TextBox>("Selected recording details", dialog);
        Assert.True(identity.IsReadOnly);
        Assert.Contains(runIds[1], identity.Text!);
        Assert.Contains("DEVICE-1-full-traceable-identity", identity.Text!);
        Assert.Contains("2026-11-04 13:30:00 +00:00", identity.Text!);
        Assert.Contains("Samples: 1", identity.Text!);
        ResponsiveShellTests.Inside(identity, dialog);
        Assert.True(identity.Focus());
        identity.SelectAll();
        Assert.Contains(runIds[1], identity.SelectedText);
        Assert.Same(original, vm.SelectedDataset);
        if (confirm)
        {
            fixture.Control<Button>("Use recording", dialog).Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        }
        else dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AuthoringUiFixture.Drain();
        Assert.Empty(fixture.Window!.OwnedWindows);
        Assert.Equal(confirm ? 1 : 0, vm.SelectedDatasetIndex);
        await fixture.WaitForPreviewAsync();
        Assert.Equal(runIds[confirm ? 1 : 0], vm.PreviewSourceName);
        Assert.Equal("Recording", vm.PreviewSourceKind);
        Assert.Equal(revision, vm.SelectedDocument!.Revision);
        var displayedName = fixture.Window!.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => AutomationProperties.GetName(text) == vm.PreviewSourceName);
        Assert.Equal(vm.PreviewSourceName, displayedName.Text);
        Assert.True(displayedName.IsEffectivelyVisible);
        ResponsiveShellTests.Inside(displayedName, fixture.Window);
    }
}
