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
   - **Station overrides** section on Run when a step is selected — **Engineer/Debug only**.
   - Mid-run operator prompts stay on the interaction host (Phase B); do not persist those values as `PlanParameterOverrides`.
   - Prompt-schema members on `OperatorInputStep` / `OperatorPromptStep` are excluded from the override listing (role `OperatorPromptSchema`).

4. **Deprecation path:** document `TrySetAcquireSettings` / `TrySetMeanGteThreshold` as sample adapters; new code uses the bridge.

5. **Tests:**
   - host set/get on sample Acquire settings; VM lists overrides only in engineer mode; overrides round-trip settings store; prompt schema excluded from station listing.

## Exit criteria

- [x] Engineer can change a sample step limit from the shell (Engineer/Debug) and see it affect the next run without saving the TapPlan.
- [x] Overrides survive app restart (`PlanParameterOverrides` in settings.json).
- [x] Operator prompt fields remain mid-run interactions, not station overrides.
## Out of scope

- Mixin-specific UX chrome (Phase D — bridge should already see embedded members if TypeData exposes them).
- Sweep iteration UI (Phase G).
