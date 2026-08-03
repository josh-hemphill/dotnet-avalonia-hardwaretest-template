namespace HardwareTest.Session.Contracts;

/// Contract plan descriptor — enum-free so each implementation maps to its own source.
public sealed class ContractPlan
{
    private ContractPlan(string id) => Id = id;

    public string Id { get; }

    /// SafeShutdown present; no loop; no operator interaction.
    public static ContractPlan Simple { get; } = new("simple");

    /// Includes a repeat/loop body (sweep demo).
    public static ContractPlan WithLoop { get; } = new("with-loop");

    /// Includes at least one operator interaction pause.
    public static ContractPlan WithInteraction { get; } = new("with-interaction");

    public override string ToString() => Id;
}
