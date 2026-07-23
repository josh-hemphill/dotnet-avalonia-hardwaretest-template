using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.OpenTap.Host;

public enum OpenTapParameterScope
{
    Plan,
    Step,
}

/// What a listed member means for the Avalonia shell.
public enum OpenTapParameterRole
{
    /// Bench setting applied via <c>PlanParameterOverrides</c> (engineer/debug).
    StationOverride,
    /// Step authoring that configures mid-run operator prompts — not a station override value.
    OperatorPromptSchema,
}

/// Which members <see cref="IOpenTapSession.EnumerateParameters"/> returns.
public enum OpenTapParameterListing
{
    /// Station-overridable settings only (default for Run board engineer panel).
    StationOverrides,
    /// Include prompt-schema authoring members (Message, field labels, …) for diagnostics.
    AllEditable,
}

/// Editable plan/step member exposed to Avalonia (station overrides, not TapPlan mutation).
public sealed class OpenTapParameterInfo
{
    public required string MemberKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Group { get; init; }
    public OperatorInteractionFieldKind Kind { get; init; } = OperatorInteractionFieldKind.String;
    public string Value { get; init; } = string.Empty;
    public bool IsExternal { get; init; }
    public bool IsReadOnly { get; init; }
    /// True when the member is flattened from a mixin / EmbedProperties embedding.
    public bool IsMixinEmbedded { get; init; }
    public OpenTapParameterRole Role { get; init; } = OpenTapParameterRole.StationOverride;
    public string? StepId { get; init; }
    public string? StepPath { get; init; }

    public static string FormatStepMemberKey(string stepId, string memberName)
        => $"{stepId}/{memberName}";

    public static string FormatPlanMemberKey(string memberName)
        => $"plan/{memberName}";

    public static bool TryParseMemberKey(string memberKey, out string ownerKey, out string memberName)
    {
        ownerKey = string.Empty;
        memberName = string.Empty;
        if (string.IsNullOrWhiteSpace(memberKey))
        {
            return false;
        }

        var slash = memberKey.LastIndexOf('/');
        if (slash <= 0 || slash >= memberKey.Length - 1)
        {
            return false;
        }

        ownerKey = memberKey[..slash];
        memberName = memberKey[(slash + 1)..];
        return !string.IsNullOrWhiteSpace(ownerKey) && !string.IsNullOrWhiteSpace(memberName);
    }
}
