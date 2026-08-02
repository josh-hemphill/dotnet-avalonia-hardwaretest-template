# Phase 11 — Session activity & stale UX

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 9](phase-9-runboard-decomposition.md), [Phase 10](phase-10-export-storage-chrome.md)
**Unblocks:** [Phase 12](phase-12-error-surfacing-chrome.md) (clearer session banners feed error hierarchy)
**Status:** Planned
**Also absorbs:** Review findings on idle/confirm-clock, Same DUT dead end, `RequireOperator` hard-coding, incomplete session strip — see [review-remediation.md](review-remediation.md)

## Goal

Make DUT session idle / stale tracking **activity-aware** and **station-configurable**: operators reviewing Results between runs are not bounced into Stale from wall-clock alone, while high-throughput lines can use short timeouts or force a DUT confirm before every run. Surface remaining time and soft-warn before hard Stale. Close the live **Same DUT / `RequireOperator`** footguns so Stale resolution always has a reachable path.

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
- Idle must be **checked on an interval** while the session is Active (not only on program load / Run start), so soft-warn and Stale appear without waiting for the next Run click.

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

### RequireOperator & Same DUT (review absorption)

- Technician field **required asterisk / placeholder** follows program `RequireOperator` on the full confirm form (today XAML always shows `Technician *`).
- **`ConfirmSameDut` validation** follows `RequireOperator`:
  - When `RequireOperator` is **false**: Same DUT must succeed without a technician name (stored or typed).
  - When `RequireOperator` is **true**: require a non-empty technician — either already on the session **or** typed in the stale prompt.
- **Stale prompt UI must not be a dead end:** when Same DUT needs a technician (required and session has none), show an inline technician field on the Stale banner — do not rely only on `NeedsDutConfirm` WrapPanel visibility. Change Session remains always available.
- Soft-warn and confirm-every-run post-run banners use the same Same DUT / Change Session resolutions (no OS dialogs).

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
- Update [adapting.md](../adapting.md) configuration table for minutes / warn % / confirm-every-run; keep hours as alias.

### C — Activity wiring

- Touch from `MainWindowViewModel.NavigateTo` and Results / ReportPreview / Inspect / Instruments / Settings meaningful commands.
- Keep Run pipeline touches; branch on confirm-every-run at completion.
- Document the activity matrix + station knobs in [adapting.md](../adapting.md) (session section) in **current** tense only after this phase ships; until then keep “Planned (Phase 11)” wording.

### D — Strip + timer UX

- Sticky DUT strip: serial, last activity relative time, countdown to soft-warn / stale (hide or simplify countdown when confirm-every-run is on and session is already pending re-confirm).
- Soft-warn banner copy: still testing this DUT? Same DUT extends activity; Change Session clears.
- Post-run banner when confirm-every-run: confirm same DUT or change before next Run.
- `DispatcherTimer` (or equivalent) while session is Active so idle/soft-warn update without Run.

### E — RequireOperator & Same DUT (expanded)

- Bind technician required UI (placeholder / asterisk / validation) to selected program `RequireOperator` on full confirm.
- Fix `ConfirmSameDut` to honor `RequireOperator` (no forced technician when false).
- When Stale and technician is required but missing from session, show technician TextBox on the Stale prompt row (not only behind `NeedsDutConfirm`).
- ViewModel tests: `RequireOperator=false` → Same DUT with empty tech succeeds; `RequireOperator=true` + empty session tech + empty input → fails with visible field path; Stale prompt exposes tech when needed.

## Exit criteria

- [ ] Idle stale uses `LastActivityAt`; confirm time remains distinct
- [ ] Idle window configurable in **minutes** (default 240); hours env/CLI still accepted
- [ ] Soft-warn percent configurable (default 80); Same DUT / Change Session still the only resolutions
- [ ] Idle/soft-warn update on an interval without requiring Run / program load
- [ ] `RequireDutConfirmEveryRun` forces re-confirm after each terminal run; next Run blocked until Same DUT / Change Session
- [ ] Opening/using Results or Report Preview between runs refreshes activity when confirm-every-run is off
- [ ] Strip shows last activity + time remaining (when applicable); interval check updates without Run
- [ ] Technician required indicator follows `RequireOperator` on full confirm
- [ ] Same DUT does not require technician when `RequireOperator` is false
- [ ] Stale + required technician with no stored name: inline field visible; Same DUT can succeed after fill; Change Session still works
- [ ] [adapting.md](../adapting.md) session section matches shipped behavior (no present-tense Phase 11 promises left as “current”)
- [ ] ViewModel + E2E: short idle soft-warn → Same DUT; confirm-every-run → second Run blocked until Same DUT; activity touch from Results; RequireOperator Same DUT cases above

## Out of scope

- Persisting operator session across process restart ([appliance-linux.md](../appliance-linux.md) — in-process only)
- Multi-DUT lanes ([Phase K](../opentap-phases/phase-k-multi-dut-parallel.md)) — per-session knobs can reuse these settings globally in K.1
- Clock / NTP discipline ([deferred-clock-discipline.md](../deferred/deferred-clock-discipline.md))
- Per-program override of confirm-every-run (station policy only in this phase)
- Sticky error severity hierarchy for non-session failures ([Phase 12](phase-12-error-surfacing-chrome.md))
- UseMockVisa hot-apply honesty ([Phase 13](phase-13-settings-live-semantics.md))
