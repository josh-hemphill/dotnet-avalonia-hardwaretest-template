using Avalonia.Controls;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public partial class RunHeaderView : UserControl
{
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
    }
}
