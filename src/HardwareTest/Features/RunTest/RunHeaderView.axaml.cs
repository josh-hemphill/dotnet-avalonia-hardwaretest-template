using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class RunHeaderView : UserControl
{
    private async void OnClearDisplayClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RunTestViewModel { CanClearRun: true } vm
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var keep = new Button { Content = "Keep display", IsCancel = true, Classes = { "operator" } };
        var clear = new Button { Content = "Clear display", Classes = { "danger" } };
        var dialog = new Window
        {
            Title = "Clear run display?",
            Width = Math.Clamp(owner.ClientSize.Width - 32, 320, 480),
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontSize = owner.FontSize,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "This clears measurements, iterations, and attempts from the Run page. The current session, program, and saved results stay available.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { keep, clear } },
                },
            },
        };
        keep.Click += (_, _) => dialog.Close(false);
        clear.Click += (_, _) => dialog.Close(true);
        dialog.Opened += (_, _) => keep.Focus();

        if (await dialog.ShowDialog<bool>(owner) && ReferenceEquals(DataContext, vm)
            && vm.ClearRunCommand is ICommand command && command.CanExecute(null))
        {
            command.Execute(null);
        }

        if (sender is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } trigger)
        {
            trigger.Focus();
        }
    }

    private async void OnReferencesClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RunTestViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            await vm.RefreshBenchReferencesAsync();
            await BenchReferenceDialog.ShowAsync(owner, "Golden references", new BenchReferenceView { DataContext = vm.References });
        }
        catch (Exception ex) { vm.SetBanner(RunBannerSeverity.Error, $"Cannot open references: {ex.Message}"); }
        (sender as Control)?.Focus();
    }
    public RunHeaderView()
    {
        InitializeComponent();
        SessionSummaryButton.Flyout!.Opened += (_, _) =>
        {
            var body = (Control)((Flyout)SessionSummaryButton.Flyout!).Content!;
            foreach (var text in body.GetVisualDescendants().OfType<TextBlock>()) text.FontSize = FontSize;
            SessionDetailsBox.FontSize = FontSize;
            SessionDetailsBox.Focus();
        };
        ProgramDetailsButton.Flyout!.Opened += (_, _) =>
        {
            ProgramDetailsBox.FontSize = FontSize;
            ProgramDetailsBox.Focus();
        };
    }
}
