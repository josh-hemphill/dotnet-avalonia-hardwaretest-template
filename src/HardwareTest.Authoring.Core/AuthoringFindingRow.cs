using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Finding with the plan that produced it. A location may be absent for plan-wide rules.
public sealed record AuthoringFindingRow(
    string ProgramId,
    string TapPlanPath,
    PlanContractFinding Finding,
    bool CanOpenProgram)
{
    public long? CheckedRevision { get; init; }
    public bool IsStale { get; init; }
    public Guid? NodeId { get; init; }
    public Guid SessionId { get; init; }
    public string? CheckedIdentity { get; init; }
    public string CheckState => $"Checked revision {CheckedRevision?.ToString() ?? "unavailable"} · {(IsStale ? "Stale — validate again" : "Current")}";
    public string NavigationLabel { get; init; } = "Open program settings";
    public string NavigationReason { get; init; } = "No precise field target was provided; opens program settings.";
    public string Severity => Finding.Severity.ToString();
    public string Code => Finding.Code;
    public string Message => Finding.Message;
    public string Location => string.IsNullOrWhiteSpace(Finding.Path) ? "Plan-wide" : Finding.Path;
}
