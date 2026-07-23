using System.ComponentModel;
using System.Globalization;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// TypeData-backed enumeration and get/set for plan/step settings (skip resources).
internal static class OpenTapParameterBridge
{
    private static readonly HashSet<string> SkippedMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "Name",
        "TypeName",
        "ChildTestSteps",
        "Parent",
        "Results",
        "EnabledChildSteps",
        "Version",
        "PlanRun",
        "StepRun",
        "Rules",
        "Error",
        "BreakConditions",
        "ForwardedMembers",
        "OpenTap.Visibility",
        "OpenTap.Description",
        "IsReadOnly",
        "Verdict",
    };

    public static string FormatStepMemberKey(string stepId, string memberName)
        => OpenTapParameterInfo.FormatStepMemberKey(stepId, memberName);

    public static string FormatPlanMemberKey(string memberName)
        => OpenTapParameterInfo.FormatPlanMemberKey(memberName);

    public static bool TryParseMemberKey(string memberKey, out string ownerKey, out string memberName)
        => OpenTapParameterInfo.TryParseMemberKey(memberKey, out ownerKey, out memberName);

    public static IReadOnlyList<OpenTapParameterInfo> Enumerate(
        object owner,
        string memberKeyPrefix,
        string? stepId,
        string? stepPath,
        bool includeReadOnly,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides)
    {
        var typeData = TypeData.GetTypeData(owner);
        var list = new List<OpenTapParameterInfo>();
        foreach (var member in typeData.GetMembers())
        {
            if (!TryDescribe(member, out var kind, out var displayName, out var group, out var isExternal, out var readOnly))
            {
                continue;
            }

            if (readOnly && !includeReadOnly)
            {
                continue;
            }

            var role = ClassifyRole(owner, member.Name);
            if (listing == OpenTapParameterListing.StationOverrides
                && role != OpenTapParameterRole.StationOverride)
            {
                continue;
            }

            object? raw = null;
            try
            {
                raw = member.Readable ? member.GetValue(owner) : null;
            }
            catch
            {
                continue;
            }

            list.Add(new OpenTapParameterInfo
            {
                MemberKey = $"{memberKeyPrefix}/{member.Name}",
                DisplayName = displayName,
                Group = group,
                Kind = kind,
                Value = FormatValue(raw, kind),
                IsExternal = isExternal,
                IsReadOnly = readOnly,
                Role = role,
                StepId = stepId,
                StepPath = stepPath,
            });
        }

        return list
            .OrderBy(p => p.Group ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// Interaction steps collect values at run time via <see cref="StepRuntime.RequestInteraction"/>;
    /// their Message/field-label properties are prompt schema, not station overrides.
    private static OpenTapParameterRole ClassifyRole(object owner, string memberName)
    {
        if (owner is not (OperatorInputStep or OperatorPromptStep))
        {
            return OpenTapParameterRole.StationOverride;
        }

        return string.Equals(memberName, nameof(ITestStep.Enabled), StringComparison.OrdinalIgnoreCase)
            ? OpenTapParameterRole.StationOverride
            : OpenTapParameterRole.OperatorPromptSchema;
    }

    public static bool TryGet(object owner, string memberName, out string? value)
    {
        value = null;
        var member = TypeData.GetTypeData(owner).GetMember(memberName);
        if (member is null || !member.Readable)
        {
            return false;
        }

        if (!TryDescribe(member, out var kind, out _, out _, out _, out _))
        {
            return false;
        }

        try
        {
            value = FormatValue(member.GetValue(owner), kind);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySet(object owner, string memberName, string value)
    {
        var member = TypeData.GetTypeData(owner).GetMember(memberName);
        if (member is null || !member.Writable)
        {
            return false;
        }

        if (!TryDescribe(member, out var kind, out _, out _, out _, out var readOnly) || readOnly)
        {
            return false;
        }

        if (!TryParseValue(value, kind, member.TypeDescriptor, out var parsed))
        {
            return false;
        }

        try
        {
            member.SetValue(owner, parsed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDescribe(
        IMemberData member,
        out OperatorInteractionFieldKind kind,
        out string displayName,
        out string? group,
        out bool isExternal,
        out bool readOnly)
    {
        kind = OperatorInteractionFieldKind.String;
        displayName = member.Name;
        group = null;
        isExternal = member is IParameterMemberData;
        readOnly = !member.Writable;

        if (string.IsNullOrWhiteSpace(member.Name) || SkippedMemberNames.Contains(member.Name))
        {
            return false;
        }

        if (!member.Readable)
        {
            return false;
        }

        foreach (var attr in member.Attributes)
        {
            var attrName = attr.GetType().Name;
            if (attrName is "AnnotationIgnoreAttribute" or "SettingsIgnoreAttribute")
            {
                return false;
            }

            switch (attr)
            {
                case BrowsableAttribute { Browsable: false }:
                    return false;
                case DisplayAttribute display:
                    if (!string.IsNullOrWhiteSpace(display.Name))
                    {
                        displayName = display.Name;
                    }

                    if (display.Group is { Length: > 0 })
                    {
                        group = string.Join(" / ", display.Group);
                    }

                    break;
            }
        }

        if (!TryMapKind(member.TypeDescriptor, out kind))
        {
            return false;
        }

        return true;
    }

    private static bool TryMapKind(ITypeData? typeDescriptor, out OperatorInteractionFieldKind kind)
    {
        kind = OperatorInteractionFieldKind.String;
        if (typeDescriptor is null)
        {
            return false;
        }

        var type = (typeDescriptor as TypeData)?.Load() ?? ResolveClrType(typeDescriptor.Name);
        if (type is null)
        {
            return false;
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(bool))
        {
            kind = OperatorInteractionFieldKind.Boolean;
            return true;
        }

        if (type == typeof(string))
        {
            kind = OperatorInteractionFieldKind.String;
            return true;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            kind = OperatorInteractionFieldKind.Number;
            return true;
        }

        return false;
    }

    private static Type? ResolveClrType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return Type.GetType(typeName, throwOnError: false)
               ?? Type.GetType($"System.{typeName}", throwOnError: false);
    }

    private static string FormatValue(object? value, OperatorInteractionFieldKind kind)
        => value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IFormattable f when kind == OperatorInteractionFieldKind.Number
                => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static bool TryParseValue(string text, OperatorInteractionFieldKind kind, ITypeData? typeDescriptor, out object? parsed)
    {
        parsed = null;
        var type = (typeDescriptor as TypeData)?.Load() ?? ResolveClrType(typeDescriptor?.Name ?? string.Empty);
        type = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;

        switch (kind)
        {
            case OperatorInteractionFieldKind.Boolean:
                if (!bool.TryParse(text, out var b))
                {
                    return false;
                }

                parsed = b;
                return true;
            case OperatorInteractionFieldKind.Number:
                if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    parsed = i;
                    return true;
                }

                if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    parsed = l;
                    return true;
                }

                if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fl))
                {
                    parsed = fl;
                    return true;
                }

                if (type == typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
                {
                    parsed = dec;
                    return true;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    parsed = type == typeof(float) ? (float)d : d;
                    if (type == typeof(int))
                    {
                        parsed = (int)d;
                    }
                    else if (type == typeof(long))
                    {
                        parsed = (long)d;
                    }

                    return true;
                }

                return false;
            default:
                parsed = text;
                return true;
        }
    }
}
