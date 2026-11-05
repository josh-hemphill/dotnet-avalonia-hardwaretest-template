using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Instruments;

/// Program filter, Run deep-link focus, and the Discover → Bind → *IDN? → Save stepper.
public partial class InstrumentsViewModel
{
    [Reactive] private string _commissionTitle = "Commission this program";
    [Reactive] private string _commissionHint = "Discover resources, bind unbound slots, query *IDN?, then Save.";
    [Reactive] private string _readinessSummary = string.Empty;
    [Reactive] private bool _showCommissionStrip = true;
    [Reactive] private int _commissionStepIndex;

    /// Applies a Run-board deep link (plan + first blocking slot).
    public void FocusProgram(string planId, string? slotName = null)
    {
        var programSlots = SlotOverrides.Where(slot =>
            string.Equals(slot.PlanId, planId, StringComparison.OrdinalIgnoreCase)).ToList();
        PlanFilter = programSlots.Count == 0 ? AllPlanFilter : PlanFilterLabel(programSlots[0]);
        ApplyPlanFilter();
        SelectedSlot = string.IsNullOrWhiteSpace(slotName)
            ? programSlots.FirstOrDefault()
            : programSlots.FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
        if (programSlots.Count == 0)
        {
            Status = $"Program {planId} was not found. Refresh slots or choose another program; no binding target is selected.";
        }
        else if (SelectedSlot is null)
        {
            Status = $"Slot {slotName} was not found in program {planId}. Refresh slots or choose a slot in this program; no binding target is selected.";
        }
        else
        {
            Status = string.IsNullOrWhiteSpace(slotName)
                ? $"Commission program {planId}."
                : $"Bind slot {slotName} for {planId}.";
        }
        RefreshCommission();
    }

    private string PlanFilterLabel(SlotOverrideItemViewModel slot)
        => SlotOverrides.Any(other =>
            !string.Equals(other.PlanId, slot.PlanId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(other.PlanDisplayName, slot.PlanDisplayName, StringComparison.OrdinalIgnoreCase))
                ? $"{slot.PlanDisplayName} (Plan ID: {slot.PlanId})"
                : slot.PlanDisplayName;

    private void RebuildPlanFilters()
    {
        var names = SlotOverrides
            .DistinctBy(s => s.PlanId, StringComparer.OrdinalIgnoreCase)
            .Select(PlanFilterLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        PlanFilterOptions.Clear();
        PlanFilterOptions.Add(AllPlanFilter);
        foreach (var name in names)
        {
            PlanFilterOptions.Add(name);
        }

        var sessionPlan = _operatorSession?.ProgramId;
        if (!string.IsNullOrWhiteSpace(sessionPlan))
        {
            var preferred = SlotOverrides.FirstOrDefault(s =>
                string.Equals(s.PlanId, sessionPlan, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                PlanFilter = PlanFilterLabel(preferred);
                return;
            }
        }

        if (!PlanFilterOptions.Contains(PlanFilter))
        {
            PlanFilter = names.Count == 1 ? names[0] : AllPlanFilter;
        }
    }

    private void ApplyPlanFilter()
    {
        VisibleSlots.Clear();
        foreach (var slot in SlotOverrides.Where(s =>
            string.Equals(PlanFilter, AllPlanFilter, StringComparison.Ordinal)
            || string.Equals(PlanFilterLabel(s), PlanFilter, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleSlots.Add(slot);
        }

        if (SelectedSlot is not null && !VisibleSlots.Contains(SelectedSlot))
        {
            SelectedSlot = VisibleSlots.FirstOrDefault();
        }

        RefreshCommission();
    }

    private bool PersistIdn(SlotOverrideItemViewModel? slot, string resource, string raw, string summary)
    {
        // A shared address is not a plan/slot identity. Only stamp the target captured
        // when the query started, and only while that exact binding still exists.
        if (_idnStore is null || slot is null || string.IsNullOrWhiteSpace(summary)
            || !SlotOverrides.Contains(slot)
            || !string.Equals(slot.EffectiveResource, resource, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _idnStore.Upsert(new StationIdnRecord
        {
            PlanId = slot.PlanId,
            SlotName = slot.SlotName,
            Resource = resource,
            IdnRaw = raw,
            IdnSummary = summary,
            QueriedAt = DateTimeOffset.UtcNow,
        });
        slot.LastIdnSummary = summary;
        return true;
    }

    private void RefreshCommission()
    {
        var snapshots = VisibleSlots.Select(s => new StationSlotSnapshot
        {
            SlotName = s.SlotName,
            PlanId = s.PlanId,
            RoleHint = s.RoleHint,
            TypeName = s.TypeName,
            EffectiveResource = s.EffectiveResource,
        }).ToList();
        var useMock = _visaModeController?.EffectiveUseMockVisa ?? _settingsStore.AppSettings.UseMockVisa;
        var report = StationReadinessEvaluator.Evaluate(snapshots, useMock);
        ReadinessSummary = report.OperatorSummary;

        if (BindingsReadOnly)
        {
            CommissionStepIndex = report.CanRun ? 3 : 1;
            CommissionTitle = report.CanRun ? "Deployment bindings" : "Deployment bindings need attention";
            CommissionHint = report.CanRun
                ? "Inspect the configured slots or query their identities. Go to Run when ready."
                : BindingOwnershipHint;
            ShowCommissionStrip = true;
            return;
        }

        if (!HasDiscoveredVisa && !HasDiscoveredOpenTap)
        {
            CommissionStepIndex = 0;
            CommissionTitle = "1 · Discover";
            CommissionHint = "Discover VISA (or OpenTAP) so you can bind plan slots.";
            ShowCommissionStrip = true;
            return;
        }

        var unbound = VisibleSlots.FirstOrDefault(s => s.Readiness.Kind == StationSlotReadinessKind.Unbound)
                      ?? VisibleSlots.FirstOrDefault(s => s.Readiness.BlocksRun);
        if (unbound is not null)
        {
            CommissionStepIndex = 1;
            CommissionTitle = "2 · Bind slot";
            CommissionHint = $"{unbound.SlotName}: select a resource, then Apply selected resource.";
            ShowCommissionStrip = true;
            return;
        }

        var needsIdn = VisibleSlots.FirstOrDefault(s => !s.HasLastIdn);
        if (needsIdn is not null)
        {
            CommissionStepIndex = 2;
            CommissionTitle = "3 · Query *IDN?";
            CommissionHint = $"{needsIdn.SlotName} is bound. Query *IDN? to confirm and record its identity.";
            ShowCommissionStrip = true;
            return;
        }

        CommissionStepIndex = 3;
        CommissionTitle = report.CanRun ? "4 · Save & ready" : "4 · Save overrides";
        CommissionHint = report.CanRun
            ? "Slots are ready. Save overrides if you changed any, then Go to Run."
            : report.OperatorSummary;
        ShowCommissionStrip = true;
    }
}
