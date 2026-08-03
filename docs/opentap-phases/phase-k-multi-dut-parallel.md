# Phase K — Multi-DUT / parallel

**Parent:** [opentap-platform.md](../opentap-platform.md)
**Depends on:** Platform [Phase 14](../platform-phases/phase-14-session-facade-split.md) (session façade split)
**Status:** Planned

## Goal

Support **more than one DUT identity** on a single station without cloning OpenTAP Editor or adding floating dialogs. Ship a usable K.1 first; keep true parallel plan execution as K.2.

## Locked product shape

### K.1 (exit criteria for this phase)

- **One OpenTAP engine process** per app instance.
- **Multiple operator DUT sessions** (identity strips / lanes): each has serial, optional part/rev, operator, confirm/stale state ([Phase 11](../platform-phases/phase-11-session-activity-stale.md) activity rules apply per session).
- **At most one plan executing at a time.** Operator selects active DUT session, then Run. Other sessions remain confirmed but idle.
- Results / run records stamp the active session’s DUT identity (existing `TestRunRecord` fields).
- No second Window; lane UI is in-panel on the Run board (or a compact session switcher in chrome).

### K.2 (same doc, out of scope for K.1 exit)

- Concurrent OpenTAP plan executions / parallel steps across DUTs.
- Requires deeper Host run isolation, VISA gate fairness, and Instrument resource contention policy — do not pretend it is free.

## Locked decisions

- Do **not** start K until Phase 14 seams exist (run vs load vs discovery).
- Station slot overrides remain **station-scoped** (shared instruments), not per-DUT, in K.1 unless a plan explicitly needs DUT-keyed overrides (defer that complexity).
- Safety Stop aborts the **active** run only in K.1; document that idle sessions are untouched.
- Session idle / stale is **per DUT session**, not global app idle.

## Workstreams

### A — Session model

- Introduce `OperatorSession` collection / `IOperatorSessionRoster` (active id + list).
- Migrate Run board from single `_session` to active session + switcher.
- Persist nothing across process restart (same as today).

### B — UI

- Run board: session chips or list (add / select / change). Confirm flow targets the selected session.
- Disable Run on non-active sessions while a run is in progress; show which session owns the live run.
- Results filters already support DUT — verify multi-session runs appear distinctly.

### C — Host

- Keep single `OpenTapSession` (or run façade) executing one plan; bind DUT identity from the active operator session on ApplyStationAndDut.
- Contract tests: switching active session between runs stamps different serials; cannot start a second run while one is active.

## Exit criteria (K.1)

- [ ] Operator can confirm ≥2 DUT sessions and switch between them without restart
- [ ] Only one plan runs at a time; second Run is blocked with clear Status
- [ ] Completed runs record the correct DUT serial for the session that ran
- [ ] Per-session idle/stale still works with Phase 11 activity rules
- [ ] Fake + real session contracts extended for “busy run blocks second start”
- [ ] E2E: confirm DUT A, run (or mock), switch to DUT B confirm, run — both in Results

## Out of scope (K.1)

- K.2 parallel plan execution / parallel OpenTAP steps across DUTs
- Per-DUT instrument resource pools
- Remote Agent / multi-station orchestration
- MES multi-unit lot workflows beyond local run history

## Future (K.2 notes)

When scheduling K.2: isolate progress listeners per run id, define Instrument open policy (exclusive vs multiplex), and extend VISA `VisaSessionGate` preempt rules for multi-run safety stop.
