# Phase 15 — Operator feedback & Settings chrome

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 12](phase-12-error-surfacing-chrome.md), [Phase 13](phase-13-settings-live-semantics.md)
**Status:** Done
**Also absorbs:** First-impression review (disabled Run without reason, Status-only pre-run blocks, crash-load Status invisible, busy affordances, empty Instruments/Report Preview, filter selection, theme-hardcoded banner/chip colors, suite completion flash) plus Settings sticky Save / About display cleanup

## Goal

Make the first session with initial users feel **responsive and honest**: every disabled control explains itself, blocking conditions use sticky severity chrome (not a whisper in the hero), async actions show busy state, empty pages guide the next click, and Settings keeps Save/Status reachable while the long engineer/diagnostics tail scrolls away. Clean up About so build identity is readable, not a redundant editable-looking form.

## Locked decisions

- **No new OS dialogs / second windows** — in-panel only ([appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned)).
- **Sticky Save chrome on Settings is in-scope and preferred.** Dock **Save + Status** (and a one-line page blurb) at the **top**; the rest of the page scrolls underneath. Debounced auto-save stays; the sticky Save is the always-visible Status home and an explicit write for operators who do not trust debounce.
  - **Do not** put Save at the bottom sticky (competes with MainWindow transport / feels buried).
  - **Do not** remove debounce as part of this phase.
- **About is display-only.** Prefer `TextBlock` (or equivalent non-input) rows — not `TextBox IsReadOnly` that still looks/focuses like an editor. Tab order skips About.
- **About Version row uses the short product version** (`BuildInfo.Version`), not `InformationalVersion` (which already embeds commit/date). Keep **Commit** and **Build (UTC)** as separate fields. Full informational string remains in Copy diagnostics / support block only.
- Pre-run and storage-critical blocks that today only set `Status` must also set a sticky **Warning** or **Error** banner (reuse `RunBannerSeverity`; start using Warning/Info which Phase 12 left unused).
- Session soft-warn / Stale panels remain distinct; severity banner must not cover or replace them.
- Busy affordances: disable the acting control (and conflicting peers where safe), change label or show a short “…” / Status — no modal spinner overlay.
- Theme: banner/chip brushes must read correctly in **Light and Dark** (map to DynamicResource or light/dark pairs — no single dark-leaning hex for both).
- Keep Feature line budgets (~600); split ViewModels/partials rather than raising the cap.
- Ship **2–3 intentional motions** only where they clarify hierarchy (banner appear, session-block expand, filter selection) — appliance-calm, not decorative noise.

## Workstreams

### A — Chrome honesty (Run + Home)

- Dynamic **Run / Run Selected** tooltips from `CanStartRun` / session / running state (“Confirm DUT first”, “Run in progress — use Safety Stop”, …). Static “Run the full suite” is insufficient when disabled.
- Route pre-run early exits in `RunExecutionViewModel` (no program, storage critical, already running, bad selection) through sticky banner **Warning/Error**, keep `Status` for in-run progress.
- Storage banner: use severity brushes; critical remains non-dismissible.
- Suite completion: brief Pass **Info**/success or Fail **Error** flash (or severity-tinted hero) in addition to completion Status prose; hide overall `ProgressBar` when idle (not only reset to 0).
- **Home crash load failure:** surface `CrashStatus` (or a non-blocking info banner) **outside** `HasCrashBanner`, so a dossier parse failure is visible. Successful crash banner uses Error/Warning severity chrome so it does not look like a “Getting started” card.
- Optionally map soft history alerts / idle soft-warn escalation into `RunBannerSeverity.Warning` when they leave Status-only (do not duplicate the orange session panel).

### B — Control affordances

- Field-level errors on DUT serial / technician Confirm Session (border + short message under the field); Confirm stays enabled but validation is visible without relying on hero Status alone.
- `IsExecuting` / busy flags on: Instruments Discover (VISA + OpenTAP), Query *IDN?*, Save overrides; Settings Save (sticky); Program load/refresh; Results refresh/reprint/export where applicable. Disable peers that would race; restore on completion/fault.
- Step filters: use `ToggleButton` (or checked `Classes`) so All / Fail / Running / Pending show selection — wire existing `filter-chip:checked` styles in `App.axaml`.

### C — Empty / onboarding states

- **Instruments:** empty-list copy + primary “Discover VISA” CTA; optional secondary link to Run.
- **Report Preview:** “No PDF loaded — open a run from Results” + navigate-to-Results CTA (same in-shell navigation pattern as Phase 12).
- Keep Home / Inspect / Results CTAs; do not regress them.

### D — Visual coherence

- Retheme `BannerBrushConverter` / `ChipBrushConverter` for Light + Dark (Fluent dynamic resources or paired brushes).
- Reuse chip styling on Results Result column (Pass/Fail color, not plain text).
- Light motion: banner appear, session-block expand, filter chip selection — 2–3 total.

### E — Settings layout & About

- Restructure `SettingsView.axaml`: top dock = title/blurb + **Save all settings** + `Status`; `ScrollViewer` hosts theme / engineer / storage / About / diagnostics / packages.
- About section: `TextBlock` rows; Version = short `BuildInfo.Version`; Commit + Build (UTC) + Runtime + RID + OpenTAP as separate readable lines; drop presenting the composite informational string in the form.
- Confirm Copy diagnostics still includes full `FormatSupportBlock` (informational version intact for support).

## Exit criteria

- [x] Disabled Run / Run Selected tooltips state the blocking reason
- [x] Pre-run / storage-critical blocks use sticky severity banner (not Status-only)
- [x] Crash dossier load failure is visible on Home without requiring `HasCrashBanner`
- [x] Discover / Save / Load / *IDN?* (and agreed peers) show busy/disabled-while-executing
- [x] DUT confirm validation visible at the field; step filters show selection
- [x] Instruments + Report Preview empty states include a next-step CTA
- [x] ProgressBar hidden (or equivalent) when idle; suite Pass/Fail has a brief severity cue
- [x] Banner/chip colors work in Light and Dark
- [x] Settings: Save + Status sticky at top; body scrolls
- [x] About is display-only TextBlocks; Version is short product version without duplicating commit/date
- [x] Feature line budgets still pass; ViewModels + architecture + host suites green

## Out of scope

- Localization / full AutomationProperties / kiosk compositor ([deferred](../deferred/deferred-appliance-kiosk.md), [deferred-localization.md](../deferred/deferred-localization.md)); operator hit-target floor shipped in [Phase 18](phase-18-operator-touch-density.md) after [Phase 17](phase-17-shell-notification-strip.md)
- Removing Settings debounce auto-save
- OpenTAP Phase K / multi-DUT
- Rewriting FluentAvalonia theme wholesale
- Remote crash upload

## Related

- First-impression investigation (agent session after Phase 14)
- [review-remediation.md](review-remediation.md) — Phase 12 leftovers (crash visibility) + new feedback cluster → this phase
- [Phase 12](phase-12-error-surfacing-chrome.md) — sticky banner foundation; Warning/Info unused until now
- [Phase 4](phase-4-build-info.md) — `BuildInfo` fields consumed by About cleanup
