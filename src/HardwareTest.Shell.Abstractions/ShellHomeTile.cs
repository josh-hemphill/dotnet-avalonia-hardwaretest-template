namespace HardwareTest.Shell;

/// Home-page contribution from a shell app. Engineer tiles follow engineer presentation.
public sealed class ShellHomeTile
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string ActionLabel { get; init; }
    public required string NavigatePageId { get; init; }
    public ShellPagePlacement Placement { get; init; } = ShellPagePlacement.Operator;
}
