using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class RunHeaderView : UserControl
{
    private async void OnReferencesClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RunTestViewModel vm || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            await vm.RefreshBenchReferencesAsync();
            await BenchReferenceDialog.ShowAsync(owner, "Golden references", new BenchReferenceView { DataContext = vm.References });
        }
        catch (Exception ex) { vm.SetBanner(RunBannerSeverity.Error, $"Cannot open references: {ex.Message}"); }
        _ = ((sender as Control)?.Focus());
    }
    public RunHeaderView()
    {
        InitializeComponent();
        SessionSummaryButton.Flyout!.Opened += (_, _) =>
        {
            var body = (Control)((Flyout)SessionSummaryButton.Flyout!).Content!;
            foreach (var text in body.GetVisualDescendants().OfType<TextBlock>()) text.FontSize = FontSize;
            SessionDetailsBox.FontSize = FontSize;
            _ = SessionDetailsBox.Focus();
        };
        ProgramDetailsButton.Flyout!.Opened += (_, _) =>
        {
            ProgramDetailsBox.FontSize = FontSize;
            _ = ProgramDetailsBox.Focus();
        };
    }
}
