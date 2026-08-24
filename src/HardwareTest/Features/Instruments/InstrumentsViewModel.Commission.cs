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
        var match = PlanFilterOptions.FirstOrDefault(p =>
            !string.Equals(p, AllPlanFilter, StringComparison.Ordinal)
            && SlotOverrides.Any(s =>
                string.Equals(s.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.PlanDisplayName, p, StringComparison.OrdinalIgnoreCase)));
        if (match is null)
        {
            var byId = SlotOverrides.FirstOrDefault(s =>
                string.Equals(s.PlanId, planId, StringComparison.OrdinalIgnoreCase));
            match = byId?.PlanDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(match))
        {
            PlanFilter = match;
        }

        ApplyPlanFilter();
        if (!string.IsNullOrWhiteSpace(slotName))
        {
            SelectedSlot = VisibleSlots.FirstOrDefault(s =>
                string.Equals(s.SlotName, slotName, StringComparison.OrdinalIgnoreCase))
                ?? SelectedSlot;
        }

        Status = string.IsNullOrWhiteSpace(slotName)
            ? $"Commission program {planId}."
            : $"Bind slot {slotName} for {planId}.";
        RefreshCommission();
    }

    private void RebuildPlanFilters()
    {
        var names = SlotOverrides
            .Select(s => s.PlanDisplayName)
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
                PlanFilter = preferred.PlanDisplayName;
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
        foreach (var slot in SlotOverrides)
        {
            if (string.Equals(PlanFilter, AllPlanFilter, StringComparison.Ordinal)
                || string.Equals(slot.PlanDisplayName, PlanFilter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleSlots.Add(slot);
            }
        }

        if (SelectedSlot is not null && !VisibleSlots.Contains(SelectedSlot))
        {
            SelectedSlot = VisibleSlots.FirstOrDefault();
        }

        RefreshCommission();
    }

    private void PersistIdn(string resource, string raw, string summary)
    {
        if (SelectedSlot is null || _idnStore is null || string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        SelectedSlot.LastIdnSummary = summary;
        _idnStore.Upsert(new StationIdnRecord
        {
            PlanId = SelectedSlot.PlanId,
            SlotName = SelectedSlot.SlotName,
            Resource = resource,
            IdnRaw = raw,
            IdnSummary = summary,
            QueriedAt = DateTimeOffset.UtcNow,
        });
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
            CommissionHint = $"{needsIdn.SlotName} is bound — Query *IDN? to record identity on this station (saved beside settings, not in AppSettings).";
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
