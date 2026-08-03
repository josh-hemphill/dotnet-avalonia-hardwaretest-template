# Phase 14 — Session façade split

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 8](phase-8-session-contract-tests.md), [Phase 9](phase-9-runboard-decomposition.md)
**Unblocks:** OpenTAP [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT)
**Status:** Done

## Goal

Split the ~29-member [`IOpenTapSession`](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs) god interface into focused surfaces so multi-DUT / parallel work does not multiply one mega-session, while keeping Phase 8 contract tests green.

## Locked decisions

- **Behavioral compatibility first.** Fake + real contract suites ([Phase 8](phase-8-session-contract-tests.md)) must keep passing; prefer adapter façades over a big-bang rewrite.
- Focused seams (in [`IOpenTapSessionSurfaces.cs`](../../src/HardwareTest.OpenTap.Host/IOpenTapSessionSurfaces.cs)):
  - **`IOpenTapPlanSession`** — load sample/board/sweep/file, step tree, loaded plan metadata, step enable / condition summary
  - **`IOpenTapRunSession`** — run / selection / pause / resume / abort / interaction (`INotifyPropertyChanged`); named to avoid collision with Core `IRunControl`
  - **`IOpenTapStationSession`** — slots, ApplyStationAndDut, parameters, sample adapters / bind
  - **`IOpenTapHostCatalog`** — device addresses, package list, plugin dirs
- Feature ViewModels take **focused interfaces via DI**. Aggregating `IOpenTapSession` remains for Phase 8 contracts and Composition registration until Phase K no longer needs it.
- No Avalonia types in Host. No new operator dialogs.
- Do **not** implement multi-DUT execution in this phase — only the seams Phase K needs.

## Workstreams

### A — Interface extraction

- Extract interfaces from current `IOpenTapSession` members without moving logic first.
- `OpenTapSession` implements all; register focused interfaces as the same singleton instance initially.

### B — Consumer migration

- Update Run board, Instruments, Settings, Inspect, Composition to depend on the narrowest interface each needs.
- Keep a deprecated aggregating `IOpenTapSession` until Phase K lands if that reduces churn.

### C — Contracts

- Retarget or duplicate Phase 8 tests per surface where valuable; keep at least one “full session” runner for regression.
- FakeOpenTapSession implements the same split.

## Exit criteria

- [x] Focused interfaces exist; Composition registers them
- [x] Feature ViewModels no longer need every session member to compile
- [x] Phase 8 real + fake contract suites green
- [x] Architecture tests still enforce Host Avalonia-free / Core OpenTAP-free
- [x] Phase K can consume run + session-identity seams without the packages API

## Implementation notes

- `IOpenTapSession` is now an empty aggregate of the four focused surfaces. Approved-surface formatting flattens inherited Host `IOpenTap*` members so the Phase 8 ratchet stays one file for the aggregate, plus per-surface snapshots.
- Composition registers `OpenTapSession` once and aliases it as `IOpenTapSession` / `IOpenTapPlanSession` / `IOpenTapRunSession` / `IOpenTapStationSession` / `IOpenTapHostCatalog`.
- Feature wiring: Settings + Instruments → catalog; Inspect → plan; MainWindow + crash Abort → run; StationOverrides → plan + station; RunExecution → run + station; RunTest coordinator → plan + run + station.
- Architecture guard: Feature sources must not mention `IOpenTapSession`.

## Out of scope

- Multiple concurrent OpenTAP plan executions (Phase K.2)
- Rewriting parameter/mixin bridges
- Remote Agent / REST
- Re-litigating Phase 9 line budgets for the Run board coordinator / UiPump (optional follow-on after this split if features still drag the mega-session into UI)

## Related

- Fresh-eyes finding map: [review-remediation.md](review-remediation.md) (façade size → this phase; Run board weight is not a Phase 11–13 gate)
