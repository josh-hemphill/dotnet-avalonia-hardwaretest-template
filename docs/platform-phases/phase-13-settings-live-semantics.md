# Phase 13 — Settings live semantics

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 3](phase-3-configuration-model.md), [Phase 10](phase-10-export-storage-chrome.md)
**Status:** Done
**Also absorbs:** Review finding — UseMockVisa save vs frozen VISA DI (silent split-brain); soft "may need restart" Status vs hard mismatch — see [review-remediation.md](review-remediation.md)

## Goal

Close the gap where Settings toggles look live but **VISA mock/real wiring is frozen at DI build**. Engineer mode already syncs via `AppSettingsSaved` (hardware readiness); this phase makes mock VISA semantics honest so operators never see "mock off" while discovery still serves `MOCK::` (and Run then blocks those resources).

## Locked decisions

- **`UseMockVisa` change must either rebuild VISA factories/discovery in-process or hard-block the toggle until restart** — no silent "looks off but still mock" path.
- Prefer **in-process rebuild** when safe (no open VISA sessions / not mid-run). If a run is active or sessions are open, refuse the change and keep the prior effective mode with clear Status copy.
- Settings UI must show **effective** mock/real mode (and provenance) after save — checkbox / diagnostics must not claim a mode the factory is not using.
- Tooltip / Status copy must be consistent: if restart is required, say so; if rebuild applied, say applied; never "may be required" when the mismatch is already present.
- Env/CLI overlays remain read-only ([Phase 3](phase-3-configuration-model.md) provenance).
- Engineer-gated dangerous settings stay behind Engineer mode (already landed); this phase does not reopen that lock-down.
- Document effective mode in Settings diagnostics / provenance after save.
- May ship **in parallel with Phase 11** (independent seam).

## Workstreams

### A — Effective VISA mode service

- Introduce a small Core (or Host-adjacent) holder for **effective** mock/real factories, registered as singleton and replaceable after save.
- On `AppSettingsSaved`, if `UseMockVisa` changed and rebuild is allowed: swap `IVisaSessionFactory` / `IVisaResourceDiscovery` implementations (or wrap with a delegating façade that can swap inner).
- Instruments page re-runs discovery after a successful swap.
- Run-board mock-resource guards must read the **same** effective mode the factory uses (no live-settings vs frozen-factory split).

### B — Unsafe change UX

- If Run is active or VISA sessions are gated busy: reject applying UseMockVisa flip; Status explains "finish or safety-stop the run, then save again" (or "restart required").
- Settings checkbox reflects effective mode (reverts on refuse). Tooltip documents live apply vs refuse — never "requires restart" for UseMockVisa.
- Align tooltip + Status strings with the actual apply path (rebuild vs refuse). Logging-sink Status may still note restart.

### C — Tests

- Unit/DI test: save with UseMockVisa flip rebuilds discovery catalog (mock catalog vs empty/throwing IVI stub).
- ViewModel test: flip refused while `IRunControl.IsRunning`.
- Regression: no path where UI shows mock off while factory still serves MockVisa* resources.

## Exit criteria

- [x] Toggling UseMockVisa either takes effect without restart or is clearly refused with reason
- [x] No path where UI shows mock off while factory still serves MockVisa*
- [x] Instruments discover reflects the new mode after a successful apply
- [x] Run mock-guards and discovery agree on effective mode
- [x] Tooltip / Status copy match the apply outcome (no soft "may be required" when mismatch exists)
- [x] Env/CLI override still wins and stays read-only

## Implementation notes

- `IVisaModeController` / `VisaModeController` in `HardwareTest.Core.Hardware` is the authoritative holder of effective mock/real mode. Registered as singleton for `IVisaModeController`, `IVisaSessionFactory`, and `IVisaResourceDiscovery`.
- `VisaSessionGate.IsBusy` added to detect open sessions.
- `SettingsViewModel.SaveAsync` calls `TryApply` before persisting; on refuse it reverts `AppSettings.UseMockVisa` and the checkbox to effective mode so the settings file never diverges from the live factory.
- `InstrumentsViewModel` subscribes to `ModeApplied` and re-discovers VISA resources automatically after a successful swap.
- `RunExecutionViewModel` mock-resource guard reads `IVisaModeController.EffectiveUseMockVisa` when available, eliminating the stale-AppSettings split after multiple saves.

## Out of scope

- Hot-reloading OpenTAP plugin directories mid-run
- Live theme / log sink rebuild beyond what already works (theme apply is live; log sinks may still note restart)
- Full settings schema migration ([deferred-schema-migration.md](../deferred/deferred-schema-migration.md))
- Operator session idle minutes UI ([Phase 11](phase-11-session-activity-stale.md))
