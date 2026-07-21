using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// One row in the flat step list (section header or leaf step).
public partial class StepListItemViewModel : ReactiveObject
{
    public static StepListItemViewModel Header(string text)
        => new() { IsHeader = true, DisplayName = text };

    public static StepListItemViewModel Leaf(HierarchyStepViewModel step)
        => new()
        {
            IsHeader = false,
            Step = step,
            DisplayName = step.Name,
        };

    public bool IsHeader { get; init; }
    public HierarchyStepViewModel? Step { get; init; }
    public string DisplayName { get; init; } = string.Empty;

    public string ChipText => Step?.ChipText ?? string.Empty;
    public string? KeyValue => Step?.KeyValue;
    public string AttemptsText => Step?.AttemptsText ?? string.Empty;
    public string Path => Step?.Path ?? string.Empty;
}

/// Status filter for the Run board step list.
public static class StepStatusFilter
{
    public const string All = "All";
    public const string Fail = "Fail";
    public const string Running = "Running";
    public const string Pending = "Pending";
}
