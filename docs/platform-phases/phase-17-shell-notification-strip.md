# Phase 17 — Shell notification strip & Run layout-shift hygiene

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 12](phase-12-error-surfacing-chrome.md), [Phase 15](phase-15-operator-feedback-chrome.md), [Phase 16](phase-16-band-focus-presentation.md)
**Unblocks:** [Phase 18](phase-18-operator-touch-density.md) (density is easier once notification chrome stops shoving content)
**Status:** Planned
**Also absorbs:** Cross-page notification consistency; Auto-row layout shift from storage/severity/crash/completion banners; Run session/interaction height thrash; DUT history blurb growing the hero after Pass/Fail

## Goal

Stop **notification chrome from shifting the work surface**, and put cross-page alerts in one **reserved shell strip** so Run, Home, and later pages share the same place for severity. Keep session confirm and operator interaction **on the Run board** (appliance rule), but cap their height so they stop shoving the step list.

## Why this exists

Today severity/storage live as **Auto-height rows at the top of Run**, crash lives only on **Home**, and completion/history text can grow the Run hero after a run. Each appear/disappear moves the step list and Details under the operator’s finger. Touch density (Phase 18) is harder while the board still jumps. Putting banners “under the detail pane” was considered and rejected as a *global* home: Details is Run-only and often closed; other pages have no equivalent pane.

## Locked decisions

- **No new OS dialogs / second windows** — in-panel only ([appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned)).
- **No toast-center framework** for blocking severity (Phase 12 preference stands). A reserved strip or overlay that does not push content is fine; modal toast theater is not.
- **Shell strip owns cross-page notifications:** storage health, Run severity (Info/Warning/Error), Home crash recovery, suite completion flash, optional history/idle soft tips that today are Status- or hero-only.
- **Reserved height even when idle.** Empty strip keeps a fixed MinHeight (~48–72px) or an equivalent spacer so content does not jump when a message appears. Idle can show a calm status pulse / blank — not `Height=0` collapse.
- **Session + interaction stay on Run.** Orange interaction host and session blocked/stale/idle panels remain in-board and reachable. Do not move Confirm Session / Continue into the shell strip.
- **Cap in-board expandable chrome.** Session form and interaction card use MaxHeight + inner ScrollViewer (or equivalent) so BoardGrid’s star region stays usable.
- **Do not put global notifications under the Details drawer.** Details is optional and Run-specific.
- Soft status that is *always* about transport (Idle / Paused / Awaiting) may stay on **nav footer** `ControlStatus` — strip is for severity and blocking awareness.
- Keep Feature line budgets (~600); prefer a small `ShellNotificationViewModel` (or MainWindow host API) over growing `RunTestViewModel`.
- Ship at most **2 intentional motions** (strip content cross-fade; optional session-panel height ease) — appliance-calm.

## Workstreams

### A — Shell notification host

- Add a reserved strip on `MainWindow` (above page content or under a thin title chrome — pick one and stick to it on every page).
- Host API: severity + message + dismissibility + optional action (e.g. “Open crashes”, “Go to Run”). Source of truth can be a shell-level service/ViewModel that Run/Home publish into.
- Migrate **storage** and **Run severity** banners off Run’s outer Auto rows into the strip.
- Migrate **Home crash** banner into the same strip (Home keeps dossier actions as buttons in the strip or a linked panel — do not leave a second competing crash card that pushes the hero).
- Route suite **completion** Info/Error into the strip instead of (or in addition to, briefly) growing the Run hero.
- Move **HistoryBanner** prose off the hero Auto row when `ShowDutHistoryOnRun` is on — strip Info or Results-only detail (prefer strip one-liner + Results for table).

### B — Run layout-shift hygiene

- Remove (or permanently collapse) Run outer Auto rows that only existed for storage/severity once strip owns them.
- Cap session blocked form and interaction host (MaxHeight + scroll). Compact session strip stays as today when not blocked.
- Keep Details/Focus star + reset pattern from Phase 16; do not reintroduce a third competing Auto notification row above the board.
- Progress bar show/hide may stay; if it still feels jumpy, reserve its 5px row always and toggle opacity.

### C — Consistency & hierarchy

- Document severity precedence when multiple producers fire (e.g. Critical storage > Error > Warning > Info; session panels remain visible underneath).
- Ensure strip does not cover Pause/Safety Stop or the session confirm controls.
- ViewModel/contract tests: publishing a banner from Run or Home sets shell state without requiring the page’s local Auto banner rows.
- Manual eval: navigate Home → Run → Results with a Warning showing — strip stays put; page content does not jump when the message appears.

### D — Docs

- Update [adapting.md](../adapting.md) / Phase 12–15 cross-links: sticky severity is shell strip, not Run-only Auto chrome.
- Note in [deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md) that strip + Phase 18 density precede full kiosk bake.

## Exit criteria

- [ ] MainWindow reserved notification strip present on every page with stable MinHeight when idle
- [ ] Storage + Run severity + Home crash + completion no longer use collapsing Auto rows that push primary content
- [ ] Session confirm / interaction remain on Run and are height-capped
- [ ] History/completion do not grow the Run hero Auto row as the primary surface
- [ ] Multi-page navigation keeps strip position and does not clobber session/interaction chrome
- [ ] ViewModels / architecture / host suites green; Feature line budgets pass

## Out of scope

- Touch MinHeight floor, splitter hit area, double-tap alternatives ([Phase 18](phase-18-operator-touch-density.md))
- Full kiosk compositor / image bake ([deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md))
- Localization / AutomationProperties pass
- Rewriting Details/Focus layout again unless strip work forces a tiny glue change
- Toast SDK / third-party notification center

## Related

- Design discussion: touch + layout shift (shell strip preferred over “under Details”)
- [Phase 12](phase-12-error-surfacing-chrome.md) — sticky severity foundation
- [Phase 15](phase-15-operator-feedback-chrome.md) — honesty / Warning+Info usage
- [Phase 16](phase-16-band-focus-presentation.md) — Details/Focus star hygiene
- [Phase 11](phase-11-session-activity-stale.md) — session panels stay distinct
