# Phase C — Plan / step parameters panel

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase B](phase-b-interaction-host.md) (reuse field editors)  
**Unblocks:** Phase D (mixin members via same bridge)

## Goal

Expose OpenTAP editable plan/step members in Avalonia so benches can adjust limits/channels without Editor. Persist station overrides separately from golden TapPlans.

## Locked rules

- Do not mutate committed `.TapPlan` files by default.
- Prefer TypeData parameter bridge over new `TrySetAcquire*` methods.
- Reuse Phase B field controls for editing.

## Work items

1. **Host API** on `IOpenTapSession` / helpers:
   - `EnumerateParameters(scope: Plan | StepPath)` → list of `{ path/id, displayName, group, kind, value, isExternal, isReadOnly }`.
   - `TryGetParameter` / `TrySetParameter`.
   - Include members OpenTAP marks as parameters / writable settings; skip resources that Instruments already owns unless useful.

2. **Station parameter overrides** in [`AppSettings`](../../src/HardwareTest.Core/Settings/AppSettings.cs):
   - e.g. `PlanParameterOverride { PlanId, MemberKey, Value }` analogous to `PlanSlotOverride`.
   - Apply on plan load / before run.

3. **UI:**
   - Parameters section on Run (pre-run) and/or Inspect when a step is selected.
   - Engineer/Debug mode can unlock more members if needed.

4. **Deprecation path:** document `TrySetAcquireSettings` / `TrySetMeanGteThreshold` as sample adapters; new code uses the bridge.

5. **Tests:** host set/get on sample Acquire settings; VM lists parameters for Fake tree; overrides round-trip settings store.

## Exit criteria

- Operator can change a sample step limit from the shell and see it affect the next run without saving the TapPlan.
- Overrides survive app restart.

## Out of scope

- Mixin-specific UX chrome (Phase D — bridge should already see embedded members if TypeData exposes them).
- Sweep iteration UI (Phase G).
