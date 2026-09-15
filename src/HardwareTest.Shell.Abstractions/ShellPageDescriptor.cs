namespace HardwareTest.Shell;

/// Catalog metadata for one shell page. The host maps <see cref="SymbolName"/> onto Fluent icons.
public sealed class ShellPageDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string SymbolName { get; init; }
    public required Type ViewModelType { get; init; }
    public required ShellPagePlacement Placement { get; init; }

    /// Parent nav id when <see cref="Placement"/> is <see cref="ShellPagePlacement.Contextual"/>.
    public string? ContextualParentId { get; init; }

    /// Reachable without a left-nav item (operator Instruments commissioning).
    public bool CanRemainWithoutNav { get; init; }

    /// Sort key among standing nav items. Built-in Settings is 90 so guests can insert before it.
    public int Order { get; init; }
}
