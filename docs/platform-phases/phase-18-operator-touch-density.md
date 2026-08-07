# Phase 18 — Operator touch density floor

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 17](phase-17-shell-notification-strip.md) (prefer strip first so density work is not fighting layout jump; may share a PR train if 17 is nearly done)
**Status:** Done
**Also absorbs:** Filter-chip / list-row undersizing; 6px GridSplitter; ToolTip-only operator tips; double-tap-only open paths; compact nav footer hit targets — previously parked under “touch-density kiosk” in Phases 12/15

## Goal

Make the shell **usable with a finger** on a bench or tablet-scale display without waiting for full appliance kiosk bake: hit targets meet a documented floor, critical tips are visible without hover, and open/detail paths are not double-tap-only. Keep motion and chrome appliance-calm.

## Why this exists

Phase 12/15 deferred touch density to [deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md), but the gaps are already painful on desktop touch and preview appliances: chips/rows at **MinHeight 28**, splitter **Height 6**, ToolTips for disabled Run / Safety meaning, and stage/Results **double-tap** with no single-tap alternative. Full kiosk (compositor, image bake) remains deferred; this phase ships the **UI density floor** that kiosk will assume.

## Locked decisions

- **No new OS dialogs / second windows.**
- **Operator control floor (v1):** interactive controls meant for operators — buttons, toggle chips, compact nav footer buttons, primary list rows — **MinHeight ≥ 40** (prefer 44 where layout allows); compact icon-only targets **≥ 40×40** (nav footer aim **48×48** to match `CompactPaneLength`).
- Engineer/debug-only dense tables may stay tighter; do not force 44px on every Settings diagnostics row.
- **ToolTip is not the only affordance** for blocking reasons (disabled Run/Run Selected, Safety Stop meaning when compact). Surface the same copy inline via shell strip (Phase 17), Status, or a one-line tip under the control.
- **Double-tap remains an accelerator**, not the sole path: stages / step hierarchy / Results report need select+Details, explicit Open, or long-press equivalent.
- **GridSplitter:** increase hit area to **≥ 12px** (preferably 16) and/or add explicit “Details taller / shorter” (or reset) controls so fingers are not required to grab a 6px line.
- Nested scroll in Details: avoid adding more nested ListBox scrollers; prefer one drawer ScrollViewer + non-scrolling short lists, or clearly separated panes (already improved in Phase 16 — do not regress).
- Keep Feature line budgets; prefer `App.axaml` style setters over per-control one-offs where possible.
- Full **AutomationProperties / Narrator / kiosk compositor** stay deferred with the appliance image plan.

## Workstreams

### A — Global density styles

- Add operator-oriented style setters (e.g. `Button`, `ToggleButton.filter-chip`, primary Run board actions) for MinHeight/MinWidth/Padding floors.
- Raise filter-chip MinHeight from 28; ensure `:pressed` as well as `:pointerover` / `:checked`.
- Compact nav footer: **48×48** icon buttons; expanded buttons keep readable labels.
- Step list / stage chip rows: bump MinHeight/Padding into the floor without turning the board into a sparse tablet mock.

### B — Splitter & Details affordances

- Widen the list↔Details `GridSplitter` hit target (padding/margin or taller bar).
- Optional: buttons to expand/collapse Details or nudge drawer share — useful when splitter is awkward with gloves.
- Confirm Phase 16 star-reset still runs after touch-driven toggles.

### C — Tips without hover

- When Run / Run Selected is disabled, show the blocking reason in the **shell strip** (Info/Warning) or a persistent one-line tip near the buttons — not ToolTip alone.
- Compact Pause/Stop: rely on strip/footer `ControlStatus` + accessible AutomationName where cheap; ToolTip may remain as secondary.
- Audit other operator ToolTip-only strings on Run / Instruments / Results empty CTAs.

### D — Gesture / open-path alternatives

- Stages: single selection already filters; ensure header Run Selected works (shipped) and double-tap is not required to open detail — wire selection + Details toggle or an explicit control.
- Step list: opening detail via Details toggle / Open when a row is selected; keep double-tap as shortcut.
- Results: single-click selects; double-click may open PDF — add an explicit **Open report** (or equivalent) that touch can hit.
- Document the gesture map in [adapting.md](../adapting.md) or testing notes (one short table).

### E — Tests & eval

- Style/contract smoke where practical (MinHeight constants or approved style snapshot — keep light).
- Manual touch or mouse-as-touch eval checklist: chips, splitter, nav compact, disabled Run tip visible, open report without double-tap.
- ViewModels / architecture green.

## Exit criteria

- [x] Operator buttons/chips/nav compact targets meet the documented MinHeight/MinWidth floor
- [x] List↔Details splitter is usable with a finger (or explicit nudge controls exist)
- [x] Disabled Run / critical transport tips visible without hover
- [x] Stage / step detail / Results report open paths work without requiring double-tap
- [x] No regression of Phase 16/17 strip and drawer behavior
- [x] Suites green; Feature line budgets pass

## Out of scope

- systemd / Cage / Weston / image bake ([deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md))
- Full screen-reader / AutomationProperties localization pass
- Glove-mode / extreme outdoor contrast themes
- Redesigning the entire Run information architecture again

## Related

- [Phase 17](phase-17-shell-notification-strip.md) — reserved strip (do first or same train)
- [Phase 12](phase-12-error-surfacing-chrome.md) / [Phase 15](phase-15-operator-feedback-chrome.md) — deferred touch notes → this phase
- [deferred-appliance-kiosk.md](../deferred/deferred-appliance-kiosk.md) — assumes this density floor later
