namespace HardwareTest.Shell;

/// One catalog entry: descriptor plus the live ViewModel instance shown in the content pane.
public sealed class ShellPage
{
    public required ShellPageDescriptor Descriptor { get; init; }
    public required object ViewModel { get; init; }
}
