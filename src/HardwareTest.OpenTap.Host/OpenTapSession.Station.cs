using System.Globalization;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

public sealed partial class OpenTapSession
{
    public bool TrySetStepEnabled(string stepPath, bool enabled)
    {
        if (IsExecuting)
        {
            return false;
        }

        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        step.Enabled = enabled;
        RefreshTreeEnabled();
        return true;
    }

    public bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (FindStepByPath(stepPath) is not AcquireVoltageStep)
        {
            return false;
        }

        var ok = true;
        if (sampleCount is > 0)
        {
            ok &= TrySetParameterForStepPath(stepPath, nameof(AcquireVoltageStep.SampleCount), sampleCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (intervalMs is >= 0)
        {
            ok &= TrySetParameterForStepPath(stepPath, nameof(AcquireVoltageStep.IntervalMs), intervalMs.Value.ToString(CultureInfo.InvariantCulture));
        }

        return ok;
    }

    public bool TrySetMeanGteThreshold(string stepPath, double threshold)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (FindStepByPath(stepPath) is not MeanGteStep)
        {
            return false;
        }

        return TrySetParameterForStepPath(
            stepPath,
            nameof(MeanGteStep.Threshold),
            threshold.ToString(CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides)
    {
        if (_plan is null)
        {
            return [];
        }

        if (scope == OpenTapParameterScope.Plan)
        {
            return OpenTapParameterBridge.Enumerate(
                _plan,
                "plan",
                stepId: null,
                stepPath: null,
                includeReadOnly,
                listing);
        }

        var step = FindStepByPath(stepPath ?? string.Empty);
        if (step is null)
        {
            return [];
        }

        var node = OpenTapStepTree.FindNode(_stepTree, step.Id.ToString());
        return OpenTapParameterBridge.Enumerate(
            step,
            step.Id.ToString(),
            step.Id.ToString(),
            node?.Path ?? stepPath,
            includeReadOnly,
            listing);
    }

    public bool TryGetParameter(string memberKey, out string? value)
    {
        value = null;
        if (!TryResolveOwner(memberKey, out var owner, out var memberName))
        {
            return false;
        }

        return OpenTapParameterBridge.TryGet(owner, memberName, out value);
    }

    public bool TrySetParameter(string memberKey, string value)
    {
        if (IsExecuting)
        {
            return false;
        }

        if (!TryResolveOwner(memberKey, out var owner, out var memberName))
        {
            return false;
        }

        var ok = OpenTapParameterBridge.TrySet(owner, memberName, value);
        if (ok && owner is ITestStep && string.Equals(memberName, nameof(ITestStep.Enabled), StringComparison.OrdinalIgnoreCase))
        {
            RefreshTreeEnabled();
        }

        return ok;
    }

    private bool TrySetParameterForStepPath(string stepPath, string memberName, string value)
    {
        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        return TrySetParameter(OpenTapParameterBridge.FormatStepMemberKey(step.Id.ToString(), memberName), value);
    }

    private bool TryResolveOwner(string memberKey, out object owner, out string memberName)
    {
        owner = null!;
        memberName = string.Empty;
        if (_plan is null || !OpenTapParameterBridge.TryParseMemberKey(memberKey, out var ownerKey, out memberName))
        {
            return false;
        }

        if (string.Equals(ownerKey, "plan", StringComparison.OrdinalIgnoreCase))
        {
            owner = _plan;
            return true;
        }

        var step = OpenTapStepTree.Flatten(_plan).FirstOrDefault(s =>
            string.Equals(s.Id.ToString(), ownerKey, StringComparison.OrdinalIgnoreCase));
        if (step is null)
        {
            return false;
        }

        owner = step;
        return true;
    }

    public bool TryGetStepConditionSummary(string stepPath, out string? summary)
    {
        summary = null;
        var step = FindStepByPath(stepPath);
        if (step is null)
        {
            return false;
        }

        var parts = new List<string>();
        switch (step)
        {
            case MeanGteStep mean:
                parts.Add($"Mean ≥ {mean.Threshold}");
                parts.Add($"Samples={mean.SampleCount}");
                parts.Add($"Enabled={mean.Enabled}");
                break;
            case AcquireVoltageStep acquire:
                parts.Add($"Samples={acquire.SampleCount}");
                parts.Add($"IntervalMs={acquire.IntervalMs}");
                parts.Add($"Enabled={acquire.Enabled}");
                break;
            default:
                parts.Add($"Enabled={step.Enabled}");
                break;
        }

        summary = string.Join(", ", parts);
        return true;
    }

    public bool TryRebindDmmResource(string resource)
        => TryBindSlotResource(_slots.FirstOrDefault()?.Name ?? "DMM", resource);

    public bool TryBindSlotResource(string slotName, string resource)
    {
        if (IsExecuting)
        {
            return false;
        }

        lock (_sync)
        {
            return TryBindSlotResource_NoLock(slotName, resource);
        }
    }

    private bool TryBindSlotResource_NoLock(string slotName, string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return false;
        }

        var slot = _slots.FirstOrDefault(s => string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));
        var instr = _instruments.FirstOrDefault(i =>
                        string.Equals(i.Name, slotName, StringComparison.OrdinalIgnoreCase))
                    ?? _instruments.FirstOrDefault();
        if (instr is null)
        {
            return false;
        }

        var trimmed = resource.Trim();
        if (!InstrumentResourceAccess.TrySetResource(instr, trimmed))
        {
            return false;
        }

        if (slot is not null)
        {
            slot.ResourceName = trimmed;
        }

        return true;
    }

}
