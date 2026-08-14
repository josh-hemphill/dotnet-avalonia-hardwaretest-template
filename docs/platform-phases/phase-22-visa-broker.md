# Phase 22 — VISA broker unification

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 19](phase-19-immediate-correctness.md), [Phase 13](phase-13-settings-live-semantics.md)
**Unblocks:** [Phase 23](phase-23-safety-opentap-worker.md) (plan I/O must go through a preemptable gate)
**Status:** Done
**Also absorbs:** Round-3 R3-13 (dual VISA paths)

## Goal

Give the process **one VISA I/O path**. Instruments-page queries, mode switch, and OpenTAP plugin steps must share a broker and a bench coordinator so a run, an `*IDN?`, and a mock/real swap cannot overlap.

## Why this exists

Core already serializes Instruments-page I/O through `VisaModeController` / `VisaSessionGate`. `VisaDmmInstrument.Open()` used `Ivi.Visa.GlobalResourceManager.Open` directly, so plan steps bypassed the gate. Mode-switch checks `IsRunning` / `IsBusy` **outside** the factory-swap lock. Safety Stop cannot preempt plan VISA because it never entered the gate.

## Locked decisions

- **One `IVisaBroker`** in Core (Avalonia-free, OpenTAP-free). Plugins take it via constructor injection or a host-owned accessor registered before `PluginManager.Search` — not a second static locator.
- **`IBenchOperationCoordinator`** (or extend `VisaModeController`) so mode swap, run start, and ID query take the same exclusive lock. No “check then swap”.
- Mock vs real remains a **process-wide** mode (Phase 13). The broker is how both modes are reached.
- Plugins must not call `GlobalResourceManager.Open` after this phase. Architecture tests should forbid `Ivi.Visa` usage outside the broker assembly/class.
- Do not block this phase on the OpenTAP worker process. In-process unification still helps Stop and mode switch even before Phase 23.
- Keep public `IOpenTap*` surfaces unchanged.

## Workstreams

### A — Broker API

- `IVisaBroker.OpenAsync(resource, ct)` returning the existing `IVisaSession` (or a thin wrapper).
- Timeout / cancel maps onto `VisaSessionGate` preemption already used by Safety Stop for Instruments I/O.

### B — Plugin injection

- `VisaDmmInstrument` (and any other IVI open) consumes `IVisaBroker`.
- Host composition registers the broker before plugin search. Tests use a fake broker.

### C — Coordinator

- Single lock for: `TryApply` mock/real, `RunAsync` / `RunSelectionAsync` start, Instruments `QuerySelectedIdnAsync` / discover.
- Fail closed with an in-panel status when the lock is held (no silent overlap).

### D — Tests & architecture

- Architecture: no `Ivi.Visa` / `GlobalResourceManager` in plugin projects.
- Host tests: plugin query goes through the fake broker; mode swap refused while a broker session is open.

## Exit criteria

- [x] Plan VISA I/O uses `IVisaBroker`; plugin IVI open is gone
- [x] Mode swap, run, and ID query cannot overlap
- [x] Architecture test forbids leftover IVI opens in plugins
- [x] Mock/real honesty from Phase 13 still holds
- [x] Host + Core tests green (serial host collection unchanged until Phase 23/24)

## Out of scope

- Killable OpenTAP worker ([Phase 23](phase-23-safety-opentap-worker.md))
- Multi-session / multi-DUT VISA ([Phase K](../opentap-phases/phase-k-multi-dut-parallel.md))
- Vendor VISA in default CI (still unproven; broker should make a future vendor job easier)

## Landed

- `VisaModeController` is the process `IVisaBroker`. Open sessions are tracked so `TryApply` refuses while a broker session is still open (in addition to `VisaSessionGate.IsBusy` / `IRunControl.IsRunning`).
- `IBenchOperationCoordinator` is fail-closed. Mode swap, run start, and Instruments `*IDN?` take the same exclusive lease; overlap returns in-panel status (or `InvalidOperationException` on the run path, which the Run board already surfaces).
- `VisaDmmInstrument` opens through `IVisaBroker` (constructor injection or `VisaBrokerHost` `SessionLocal` registered inside `OpenTapPluginSearch.SearchSerialized` before `PluginManager.Search`). Plugins.Basic no longer references `IviFoundation.Visa`.
- Architecture scans plugin `.cs` / `.csproj` for `Ivi.Visa`, `GlobalResourceManager`, and `IviFoundation.Visa`. Core IVI stays in `VisaFactories.cs` / `VisaResourceDiscovery.cs`.
- Public `IOpenTap*` surfaces are unchanged. Mid-call Write/Query still cannot be aborted in-process; hung plan I/O is preempted by killing the OpenTAP worker ([Phase 23](phase-23-safety-opentap-worker.md)).

## Related

- [phase-13-settings-live-semantics.md](phase-13-settings-live-semantics.md)
- Core `VisaSessionGate` / `VisaModeController`
