using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Finding with the plan that produced it. A location may be absent for plan-wide rules.
public sealed record AuthoringFindingRow(
    string ProgramId,
    string TapPlanPath,
    PlanContractFinding Finding,
    bool CanOpenProgram)
{
    public string Severity => Finding.Severity.ToString();
    public string Code => Finding.Code;
    public string Message => Finding.Message;
    public string Location => string.IsNullOrWhiteSpace(Finding.Path) ? "Plan-wide" : Finding.Path;
}
