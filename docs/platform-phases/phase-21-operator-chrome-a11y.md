# Phase 21 — Operator chrome & accessibility

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 18](phase-18-operator-touch-density.md), [Phase 19](phase-19-immediate-correctness.md) (Stop Run copy, chip contrast, Settings names)
**Status:** Done
**Also absorbs:** Round-3 R3-12 (Run density, compact tooltips, live regions)

## Goal

Make the shell **readable and operable** on a bench display: Run board operational text is large enough, disabled/compact controls expose their meaning without hover, Settings is sectioned for scanning, and status changes are announced to assistive tech via live regions. This is the accessibility floor that kiosk bake will assume — not a full Narrator certification.

## Why this exists

Phase 18 raised hit targets. Phase 19 fixed chip contrast and Settings control names. The Run board is still dense (10–11px operational text), compact Pause/Stop `ToolTip.Tip` did not appear in live compact-nav testing, and there are no live regions for run/status changes.

## Locked decisions

- **No new OS dialogs / second windows.**
- **Do not redesign the Run information architecture** (that was Phases 9/16/17). Density work is type scale, spacing, and what is visible vs tooltip-only.
- Compact icon-only Pause/Stop: **visible label or persistent footer text**, not ToolTip alone (Phase 18 already required this; live testing showed a gap).
- Live regions: `AutomationProperties.LiveSetting` on the shell strip and Run status line (polite for status, assertive for Stop / interaction).
- Settings: visual **section headings** that are also headings in the automation tree; keep one page (no settings window).
- Engineer/debug dense tables may stay tighter.
- Full screen-reader certification, localization, and kiosk compositor stay deferred with [appliance kiosk](../deferred/deferred-appliance-kiosk.md).

## Workstreams

### A — Run type scale

- Raise operational chip/step/hero secondary text from 10–11px toward **12–13px** (or equivalent theme resource) without blowing the 600-line Feature budget — prefer `App.axaml` resources.
- Recheck Phase 18 MinHeight floors after type scale.

### B — Compact transport chrome

- Reproduce compact-nav Pause/Stop with the pane collapsed; if ToolTips still do not show, add `AutomationProperties.Name` (already present) **and** keep `ControlStatus` / strip copy in sync so the meaning is on-screen.
- Disabled Run / Stop: blocking reason already on `CanStartRunTip`; verify compact mode still shows it (strip or one-line tip).

### C — Live regions

- Shell notification strip: polite live region.
- Interaction host / Stop in progress: assertive.
- Avoid announcing plot-sample floods.

### D — Settings scanability

- Group Theme / Engineer / Storage / About / Diagnostics / Plugins as labeled sections (`AutomationProperties.HeadingLevel` where Avalonia supports it).
- Do not split into multiple pages in this phase.

## Exit criteria

- [x] Run operational text meets the documented type floor on Light and Dark
- [x] Compact Pause/Stop meaning is available without hover
- [x] Status / strip / interaction changes are live-region backed
- [x] Settings sections are headings in the automation tree
- [x] Phase 18 touch floors and Phase 19 contrast/names do not regress
- [x] ViewModels + E2E green

## Out of scope

- WCAG AAA / outdoor glove contrast themes
- Localization ([deferred-localization.md](../deferred/deferred-localization.md))
- systemd / Cage / Weston image bake
- Changing Safety Stop into a real interlock ([Phase 23](phase-23-safety-opentap-worker.md))

## Landed

- `OperatorTouchDensity.OperationalFontSize` is 12px; Run chips/steps/hero secondary and compact captions bind to it (`TextBlock.op-type` in `App.axaml`).
- Compact-nav Pause/Stop keep 48×48 targets and show `PauseResumeLabel` / `SafetyStopLabel` plus `ControlStatus` without hover.
- Shell notification strip is a polite live region; Run status is polite unless Stop / operator prompt (assertive). Interaction host is assertive. Hero status is not a live region.
- Settings keeps one page; Theme / Engineer / Storage / About / Diagnostics / OpenTAP packages are `HeadingLevel` 2.

## Related

- [phase-18-operator-touch-density.md](phase-18-operator-touch-density.md)
- [phase-19-immediate-correctness.md](phase-19-immediate-correctness.md)
- [phase-17-shell-notification-strip.md](phase-17-shell-notification-strip.md)
