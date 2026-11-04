using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HardwareTest.Features.Home;
using HardwareTest.Features.Settings;
using Xunit;

namespace HardwareTest.E2E.Tests;

[Collection("E2E")]
public sealed class OperatorSettingsWorkflowTests
{
    [AvaloniaFact]
    public async Task Actual_async_settings_save_publishes_engineer_navigation_and_feedback_on_the_UI_thread()
    {
        using var shown = E2EHarness.ShowMainWindow();
        var main = shown.Main;
        main.NavigateToPageId("Home");
        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        main.NavigateToPageId("Settings");
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        var original = settings.IsEngineerDebugMode;
        var offThread = 0;
        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref offThread);
        }
        settings.PropertyChanged += Observe;
        home.PropertyChanged += Observe;
        main.RunTest.PropertyChanged += Observe;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler navigationChanged = (_, _) =>
        {
            if (!Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref offThread);
        };
        main.NavigationItems.CollectionChanged += navigationChanged;
        try
        {
            settings.IsEngineerDebugMode = true;
            await settings.SaveCommand.ExecuteAsync();
            await E2EHarness.PumpUiAsync();
            Assert.False(settings.IsBusy);
            Assert.Contains("Saved at", settings.Status);
            Assert.True(home.IsEngineerMode);
            Assert.True(main.RunTest.IsEngineerDebugMode);
            Assert.Contains(main.NavigationItems, item => item.Id == "Instruments");
            Assert.Equal(0, offThread);
        }
        finally
        {
            settings.PropertyChanged -= Observe;
            home.PropertyChanged -= Observe;
            main.RunTest.PropertyChanged -= Observe;
            main.NavigationItems.CollectionChanged -= navigationChanged;
            settings.IsEngineerDebugMode = original;
            await settings.SaveCommand.ExecuteAsync();
            await E2EHarness.PumpUiAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(14)]
    [InlineData(20)]
    public async Task Compact_settings_exposes_readiness_action_before_secondary_configuration_and_preserves_the_plan(int font)
    {
        using var shown = E2EHarness.ShowMainWindow();
        var main = shown.Main;
        var window = shown.Window;
        window.Width = 900; window.Height = 600; window.FontSize = font;
        main.IsNavPaneOpen = false;
        var selected = main.RunTest.ProgramSelection.SelectedProgram;
        main.NavigateToPageId("Settings");
        await E2EHarness.PumpUiAsync();
        var view = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
        var engineer = view.GetVisualDescendants().OfType<Expander>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == "Engineer settings");
        Assert.False(engineer.IsExpanded);
        var open = view.GetVisualDescendants().OfType<Button>()
            .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == "Open station readiness on Run");
        Assert.True(open.IsEffectivelyVisible);
        var position = open.TranslatePoint(default, window);
        Assert.NotNull(position);
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(position.Value, open.Bounds.Size)));
        open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await E2EHarness.PumpUiAsync();
        Assert.Same(main.RunTest, main.CurrentPage);
        Assert.Same(selected, main.RunTest.ProgramSelection.SelectedProgram);
        Assert.False(main.RunTest.IsRunning);
    }
}
