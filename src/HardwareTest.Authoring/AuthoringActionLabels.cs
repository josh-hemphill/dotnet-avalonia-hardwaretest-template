using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;

namespace HardwareTest.Authoring;

/// Wrap string action labels inside the real button content width while retaining Content identity.
public static class AuthoringActionLabels
{
    public static IDataTemplate WrappedText { get; } = new FuncDataTemplate<string>((label, _) => new TextBlock
    {
        Text = label,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    });
}
