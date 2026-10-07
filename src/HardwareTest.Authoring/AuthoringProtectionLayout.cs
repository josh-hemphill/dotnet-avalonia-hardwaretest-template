using Avalonia.Controls;
using Avalonia.Layout;

namespace HardwareTest.Authoring;

/// Measured decision rows allow larger type and translated labels to wrap without hiding choices.
internal static class AuthoringProtectionLayout
{
    public static StackPanel Decisions(params Button[] decisions)
    {
        var rows = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var decision in decisions)
        {
            decision.HorizontalAlignment = HorizontalAlignment.Stretch;
            decision.ContentTemplate = AuthoringActionLabels.WrappedText;
            rows.Children.Add(decision);
        }
        return rows;
    }
}
