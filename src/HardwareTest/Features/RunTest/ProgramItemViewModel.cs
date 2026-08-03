using System.Collections.Generic;
using HardwareTest.OpenTap.Host;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

/// One selectable program in the Run board catalog dropdown.
public partial class ProgramItemViewModel : ReactiveObject
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Path { get; init; }
    public string DutFamily { get; init; } = "generic";
    public bool IsSample { get; init; }
    public ProgramLoadKind LoadKind { get; init; } = ProgramLoadKind.TapPlanFile;
    public ProgramRequirements Requirements { get; init; } = ProgramRequirements.Sample;
    public IReadOnlyList<string> ReportKinds { get; init; } = [HardwareTest.Core.Runs.ReportKinds.Status];
    /// When true (default), Run Selected keeps SafeShutdown enabled.
    public bool SelectionIncludesCleanup { get; init; } = true;
}
