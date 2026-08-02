# Phase 13 — Settings live semantics

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 3](phase-3-configuration-model.md), [Phase 10](phase-10-export-storage-chrome.md)
**Status:** Planned

## Goal

Close the gap where Settings toggles look live but **VISA mock/real wiring is frozen at DI build**. Engineer mode already syncs via `AppSettingsSaved` (hardware readiness); this phase makes mock VISA semantics honest.

## Locked decisions

- **`UseMockVisa` change must either rebuild VISA factories/discovery in-process or hard-block the toggle until restart** — no silent “looks off but still mock” path.
- Prefer **in-process rebuild** when safe (no open VISA sessions / not mid-run). If a run is active or sessions are open, refuse the change and keep the prior effective mode with clear Status copy.
- Env/CLI overlays remain read-only ([Phase 3](phase-3-configuration-model.md) provenance).
- Engineer-gated dangerous settings stay behind Engineer mode (already landed); this phase does not reopen that lock-down.
- Document effective mode in Settings diagnostics / provenance after save.

## Workstreams

### A — Effective VISA mode service

- Introduce a small Core (or Host-adjacent) holder for **effective** mock/real factories, registered as singleton and replaceable after save.
- On `AppSettingsSaved`, if `UseMockVisa` changed and rebuild is allowed: swap `IVisaSessionFactory` / `IVisaResourceDiscovery` implementations (or wrap with a delegating façade that can swap inner).
- Instruments page re-runs discovery after a successful swap.

### B — Unsafe change UX

- If Run is active or VISA sessions are gated busy: reject applying UseMockVisa flip; Status explains “finish or safety-stop the run, then save again” (or “restart required”).
- Settings checkbox should reflect **effective** mode when rebuild was refused.

### C — Tests

- Unit/DI test: save with UseMockVisa flip rebuilds discovery catalog (mock catalog ↔ empty/throwing IVI stub).
- ViewModel test: flip refused while `IRunControl.IsRunning`.

## Exit criteria

- [ ] Toggling UseMockVisa either takes effect without restart or is clearly refused with reason
- [ ] No path where UI shows mock off while factory still serves MockVisa*
- [ ] Instruments discover reflects the new mode after a successful apply
- [ ] Env/CLI override still wins and stays read-only

## Out of scope

- Hot-reloading OpenTAP plugin directories mid-run
- Live theme / log sink rebuild beyond what already works (theme apply is live; log sinks may still note restart)
- Full settings schema migration ([deferred-schema-migration.md](../deferred/deferred-schema-migration.md))
