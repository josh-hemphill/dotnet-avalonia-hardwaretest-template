# Phase 11 — Session activity & stale UX

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 9](phase-9-runboard-decomposition.md), [Phase 10](phase-10-export-storage-chrome.md)
**Unblocks:** [Phase 12](phase-12-error-surfacing-chrome.md) (clearer session banners feed error hierarchy)
**Status:** Planned

## Goal

Make DUT session idle / stale tracking **activity-aware** and **station-configurable**: operators reviewing Results between runs are not bounced into Stale from wall-clock alone, while high-throughput lines can use short timeouts or force a DUT confirm before every run. Surface remaining time and soft-warn before hard Stale. Fix `RequireOperator` labeling.

## Locked decisions

### Activity vs identity

- Split **identity confirm time** (`ConfirmedAt`) from **last operator activity** (`LastActivityAt`) on [`OperatorSession`](../../src/HardwareTest.OpenTap.Host/OperatorSession.cs). Today [`TouchActivity`](../../src/HardwareTest.OpenTap.Host/OperatorSession.cs) only refreshes `ConfirmedAt`.
- Idle stale uses **`LastActivityAt`**, not confirm time (except confirm-every-run mode below).
- **Activity touches** (bump `LastActivityAt` while `State == Active`), unless confirm-every-run makes post-run stale immediate:
  - DUT Confirm / Same DUT
  - Run start and Run complete (Pass/Fail/Cancel/Error) — see confirm-every-run exception
  - Transport Continue (operator interaction)
  - In-app navigation to a page **and** meaningful page use: Results list/open, Report Preview load, Inspect refresh, Instruments discover/save, Settings save
- Viewing reports / history **does** count as activity when idle-timer mode is on — do not invalidate a confirmed DUT while the operator is still using the shell between runs.
- Pure wall-clock with **no** shell interaction may still stale after the configured idle window.

### Configurable idle window (high throughput)

- Canonical setting is **`OperatorSessionIdleMinutes`** (int), persisted and overridable via env/CLI. Default **240** (4 hours) to match today’s default.
- Migrate / alias existing **`OperatorSessionIdleHours`**: read path accepts hours for back-compat (env `HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS`, CLI `--session-idle-hours`); write path prefers minutes (`HARDWARETEST_OPERATOR_SESSION_IDLE_MINUTES`, `--session-idle-minutes`). Settings UI edits minutes (with optional hours display helper).
- Allowed range: **1–10080** minutes (1 minute through 7 days). Values outside clamp with Status/provenance detail.
- Soft-warn fraction is configurable: **`OperatorSessionIdleWarnPercent`** (int, default **80**, clamp 50–95). Soft-warn fires when elapsed ≥ percent of idle minutes. In-panel only. Resolutions remain **Same DUT** / **Change Session**. No OS dialogs.
- UI-side interval timer scales with the window (e.g. every 15–60 s, or ≤10% of idle minutes) and calls `CheckIdleStale` so banners update without waiting for Run.
- Session strip shows **last activity** and **time remaining** until soft-warn and until hard Stale.

### Confirm every run (force ask before each test)

- Add **`RequireDutConfirmEveryRun`** (bool, default **false**), Engineer-gated in Settings, overridable via env/CLI (`HARDWARETEST_REQUIRE_DUT_CONFIRM_EVERY_RUN`, `--require-dut-confirm-every-run`).
- When **true**: after each run reaches a terminal outcome (Pass/Fail/Error/Cancelled), transition the active session to **Stale** (or equivalent “re-confirm required”) so the next Run cannot start until **Same DUT** or **Change Session** / full confirm. Do **not** treat Run-complete as an activity touch that extends idle in this mode.
- When **true**, idle soft-warn / countdown are secondary; the post-run re-confirm is the primary gate. Idle timer may still stale a parked session that never runs.
- Confirm-every-run is **station policy**, not per-program sidecar (keeps high-churn lines consistent across plans). Program `requireSerial` / `requireOperator` still apply on the confirm form.

### RequireOperator

- Technician field required asterisk / validation follows program `RequireOperator` (today XAML always shows required).

## Workstreams

### A — Core session model

- Add `LastActivityAt`; set on Confirm / Same DUT alongside `ConfirmedAt`.
- Change `TouchActivity` to update `LastActivityAt` only (keep `ConfirmedAt` as identity stamp).
- `CheckIdleStale` compares against `LastActivityAt` using effective idle `TimeSpan` from minutes.
- Expose soft-warn state (e.g. `IsIdleWarning` / remaining `TimeSpan`) using `OperatorSessionIdleWarnPercent`.
- On run terminal + `RequireDutConfirmEveryRun`: mark Stale and skip activity extend.

### B — Settings / binder

- Add `OperatorSessionIdleMinutes`, `OperatorSessionIdleWarnPercent`, `RequireDutConfirmEveryRun` to `AppSettings` + Phase 3 binder + provenance.
- Keep `OperatorSessionIdleHours` as compatibility alias (derive or dual-bind; document precedence: minutes override wins if both set).
- Settings UI (Engineer section): idle minutes, warn %, confirm-every-run checkbox + short tooltips for high-throughput vs long-dwell benches.
- Relax Settings clamp that today forces hours ∈ [1, 168].

### C — Activity wiring

- Touch from `MainWindowViewModel.NavigateTo` and Results / ReportPreview / Inspect / Instruments / Settings meaningful commands.
- Keep Run pipeline touches; branch on confirm-every-run at completion.
- Document the activity matrix + station knobs in [adapting.md](../adapting.md) (session section).

### D — Strip + timer UX

- Sticky DUT strip: serial, last activity relative time, countdown to soft-warn / stale (hide or simplify countdown when confirm-every-run is on and session is already pending re-confirm).
- Soft-warn banner copy: still testing this DUT? Same DUT extends activity; Change Session clears.
- Post-run banner when confirm-every-run: confirm same DUT or change before next Run.
- `DispatcherTimer` (or equivalent) while session is Active.

### E — RequireOperator

- Bind technician required UI to selected program requirements (`RequireOperator`), not a hard-coded `*`.

## Exit criteria

- [ ] Idle stale uses `LastActivityAt`; confirm time remains distinct
- [ ] Idle window configurable in **minutes** (default 240); hours env/CLI still accepted
- [ ] Soft-warn percent configurable (default 80); Same DUT / Change Session still the only resolutions
- [ ] `RequireDutConfirmEveryRun` forces re-confirm after each terminal run; next Run blocked until Same DUT / Change Session
- [ ] Opening/using Results or Report Preview between runs refreshes activity when confirm-every-run is off
- [ ] Strip shows last activity + time remaining (when applicable); interval check updates without Run
- [ ] Technician required indicator follows `RequireOperator`
- [ ] ViewModel + E2E: short idle soft-warn → Same DUT; confirm-every-run → second Run blocked until Same DUT; activity touch from Results

## Out of scope

- Persisting operator session across process restart ([appliance-linux.md](../appliance-linux.md) — in-process only)
- Multi-DUT lanes ([Phase K](../opentap-phases/phase-k-multi-dut-parallel.md)) — per-session knobs can reuse these settings globally in K.1
- Clock / NTP discipline ([deferred-clock-discipline.md](../deferred/deferred-clock-discipline.md))
- Per-program override of confirm-every-run (station policy only in this phase)
