# Phase 12 — Error surfacing & chrome polish

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 9](phase-9-runboard-decomposition.md), [Phase 11](phase-11-session-activity-stale.md)
**Status:** Planned
**Also absorbs:** Review findings on Status overwrite, async UI marshaling, Run-while-running, progress 100%, fail-filter chip, PDF dispose, interaction Cancel confusion, Home/Inspect/Results wayfinding, `PostToUi` swallow, crash-banner silent catch — see [review-remediation.md](review-remediation.md)

## Goal

Make operator-visible failures hard to miss, keep async faults on the UI thread, and clean up Run board / transport / preview / secondary-page chrome that confuses operators under stress. Close the non-session UX gaps from the fresh-eyes review without reopening session idle (owned by Phase 11).

## Locked decisions

- **No new OS dialogs / second windows** for errors — in-panel only ([appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned)).
- Prefer a **sticky in-panel error/status banner** (or severity-aware hero Status) over silent overwrite of the run status line.
- Interaction validation stays on the orange host; do not permanently clobber the run hero with field errors.
- Fire-and-forget `Observe` / `ContinueWith` paths must marshal to the Avalonia UI dispatcher (same pattern as the Run board UI pump).
- **`PostToUi` must not silently run work on a random thread** when the dispatcher throws — prefer log + drop or a single documented fallback that still targets the UI scheduler fake in tests.
- OpenTAP host suite flake under full serial load may be noted in tests docs; fixing host flake is **not** the exit gate for this phase unless a regression is introduced here.
- Wayfinding CTAs on Home / empty Inspect / empty Results are **in-shell navigation** (set current page), not new windows.

## Workstreams

### A — Error hierarchy

- Introduce a small severity model on the Run board (and optionally MainWindow chrome): Info / Warning / Error.
- Errors from Run pipeline, Instruments, Results export, Settings save surface in a dismissible or auto-clearing banner with clear copy.
- Keep `Status` for transient progress; do not rely on a single overwritten string for blocking failures.
- Session soft-warn / Stale banners from Phase 11 remain distinct; severity banner must not hide them.

### B — Async UI safety

- Audit `Observe`, `ScheduleOpenDetail`, and similar `ContinueWith(..., TaskScheduler.Default)` call sites in feature ViewModels.
- Marshal fault handlers and property updates onto the UI scheduler used by the Run board pump.
- Fix `PostToUi` / equivalent: do not swallow dispatcher failures into `action()` on the wrong thread; log and either retry on UI or no-op with Status if appropriate.
- Add a ViewModel test that a faulted observed task sets Status/Error on the UI scheduler fake.

### C — Run board chrome polish

- Disable **Run** / **Run Selected** while `IsRunning` (not only SessionBlocked); Cancel/Safety Stop remain the abort path.
- After suite Fail auto-filters the step list, show an explicit filter chip / status so operators know why the list shrank; easy clear back to All.
- Reset overall progress to **0** (or hide bar) when returning to Idle after completion **or** early exit (unbound slots, mock blocked) — never leave the bar stuck at 100% for a run that did not execute.
- Report Preview: dispose prior `Bitmap`s when clearing pages (leak under reprint).

### D — Interaction host clarity

- Make Cancel / decline of an in-panel prompt **visually distinct** from Pause and from global Safety Stop (label + tooltip): e.g. “Cancel prompt (aborts run)” vs “Safety Stop”.
- Keep appliance rule: Cancel of a required interaction still maps to existing safety-stop / `Response.Cancelled` — this workstream is **copy and placement**, not a new soft-decline contract unless Host already supports it.
- Do not permanently overwrite the run hero with field validation (stay on the orange host).

### E — Wayfinding & secondary empty states

- **Home:** replace pure info cards with (or add) clear CTAs — e.g. Open Run, Open Instruments, Open Results — wired to existing navigation.
- **Inspect** empty (“no plan loaded”): one short line + button/link to Run.
- **Results** empty: short empty copy; keep dense grid as-is unless a trivial header clarity win appears.
- **Home crash banner:** stop bare `catch { HasCrashBanner = false; }` — on dossier load failure, leave banner off **or** show a non-blocking Status/detail that load failed (prefer visibility over silent hide).

## Exit criteria

- [ ] Blocking errors appear in-panel with severity; not only a fleeting Status overwrite
- [ ] Observed task faults update UI on the dispatcher; regression test exists
- [ ] `PostToUi` (or equivalent) does not silently execute UI mutations off-thread
- [ ] Run / Run Selected disabled while running; fail-filter is visible/clearable; progress resets when idle **and** after early-exit paths
- [ ] Report Preview page clear disposes bitmaps
- [ ] Interaction Cancel / Safety Stop labeling is distinct under stress
- [ ] Home has navigation CTAs; Inspect (and Results empty) offer a path back to Run
- [ ] Crash dossier load failure is not silently discarded
- [ ] E2E smoke still green

## Out of scope

- Toast / notification center frameworks
- Telemetry of error rates
- Rewriting the entire OpenTAP host serial test suite for flake
- Session idle / Same DUT / `RequireOperator` (Phase 11)
- UseMockVisa rebuild (Phase 13)
- Full Accessibility / AutomationProperties / touch-target pass ([deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md))
- Localization ([deferred-localization.md](../deferred/deferred-localization.md))
