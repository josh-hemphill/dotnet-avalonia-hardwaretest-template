# Phase 12 — Error surfacing & chrome polish

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 9](phase-9-runboard-decomposition.md), [Phase 11](phase-11-session-activity-stale.md)
**Status:** Planned

## Goal

Make operator-visible failures hard to miss, keep async faults on the UI thread, and clean up small Run board / transport / preview chrome issues that confuse operators under stress.

## Locked decisions

- **No new OS dialogs / second windows** for errors — in-panel only ([appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned)).
- Prefer a **sticky in-panel error/status banner** (or severity-aware hero Status) over silent overwrite of the run status line.
- Interaction validation stays on the orange host; do not permanently clobber the run hero with field errors.
- Fire-and-forget `Observe` / `ContinueWith` paths must marshal to the Avalonia UI dispatcher (same pattern as the Run board UI pump).
- OpenTAP host suite flake under full serial load may be noted in tests docs; fixing host flake is **not** the exit gate for this phase unless a regression is introduced here.

## Workstreams

### A — Error hierarchy

- Introduce a small severity model on the Run board (and optionally MainWindow chrome): Info / Warning / Error.
- Errors from Run pipeline, Instruments, Results export, Settings save surface in a dismissible or auto-clearing banner with clear copy.
- Keep `Status` for transient progress; do not rely on a single overwritten string for blocking failures.

### B — Async UI safety

- Audit `Observe`, `ScheduleOpenDetail`, and similar `ContinueWith(..., TaskScheduler.Default)` call sites in feature ViewModels.
- Marshal fault handlers and property updates onto the UI scheduler used by the Run board pump.
- Add a ViewModel test that a faulted observed task sets Status/Error on the UI scheduler fake.

### C — Chrome polish

- Disable **Run** / **Run Selected** while `IsRunning` (not only SessionBlocked); Cancel/Safety Stop remain the abort path.
- After suite Fail auto-filters the step list, show an explicit filter chip / status so operators know why the list shrank; easy clear back to All.
- Reset overall progress to 0 (or hide bar) when returning to Idle after completion — do not leave the bar stuck at 100%.
- Report Preview: dispose prior `Bitmap`s when clearing pages (leak under reprint).

## Exit criteria

- [ ] Blocking errors appear in-panel with severity; not only a fleeting Status overwrite
- [ ] Observed task faults update UI on the dispatcher; regression test exists
- [ ] Run disabled while running; fail-filter is visible/clearable; progress resets when idle
- [ ] Report Preview page clear disposes bitmaps
- [ ] E2E smoke still green

## Out of scope

- Toast / notification center frameworks
- Telemetry of error rates
- Rewriting the entire OpenTAP host serial test suite for flake
