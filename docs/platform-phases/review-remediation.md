# Review remediation map (fresh-eyes findings → phases)

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Planning (this doc only — implementation tracks the phase plans below)
**Source:** Fresh-context codebase review (operator UX, correctness, doc drift)

This map routes every finding into an existing phase (or deferred). **Overlaps with session/idle/Same DUT/`RequireOperator` are absorbed into [Phase 11](phase-11-session-activity-stale.md)** so finishing Phase 11 closes that cluster in one pass. Non-session chrome, async safety, and VISA honesty stay in 12 / 13. Façade weight stays in 14. Localization / kiosk / touch density stay deferred.

## Priority order (implementation)

| Order | Phase | Why first |
| --- | --- | --- |
| 1 | **[13](phase-13-settings-live-semantics.md)** (can parallelize) | UseMockVisa split-brain is a silent operator trap today |
| 1 | **[11](phase-11-session-activity-stale.md)** | Idle/Same DUT/`RequireOperator` are live session footguns; unblocks 12 banners |
| 2 | **[12](phase-12-error-surfacing-chrome.md)** | Sticky errors, UI-thread faults, Run-while-running, wayfinding |
| 3 | **[14](phase-14-session-facade-split.md)** | Before OpenTAP [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) |
| — | Doc hygiene (this PR / with each phase) | README phase ranges; adapting tense; checklist status |
| — | [Deferred](../deferred/) | Localization, appliance kiosk/touch, clock discipline |

Phases **11** and **13** may proceed in parallel (independent seams). Phase **12** depends on **11** for session-banner hierarchy. Phase **14** may start after **8/9** but should land before **K**. Phase **15** (operator feedback + Settings chrome) follows **12/13**.

## Finding → destination

### Critical / high

| Finding | Destination | Notes |
| --- | --- | --- |
| UseMockVisa DI freeze vs live settings | **Phase 13** | Rebuild or hard-refuse; Settings shows **effective** mode |
| Idle/stale keys off `ConfirmedAt`; no activity / timer | **Phase 11** A–D | Already core of Phase 11 |
| Idle only checked on load/Run, not while browsing | **Phase 11** C–D | Interval timer + activity touches |
| `RequireOperator` always `*` in XAML | **Phase 11** E | Bind asterisk / validation to program req |
| Same DUT always requires technician; Stale hides inputs | **Phase 11** E (expanded) | Show tech field when needed; skip when not required |
| `Observe` / `ScheduleOpenDetail` on `TaskScheduler.Default` | **Phase 12** B | Marshal to UI dispatcher |
| Progress forced to 100% in `finally` (incl. failed starts) | **Phase 12** C | Reset/hide when idle; don’t fake completion |

### Medium UX

| Finding | Destination | Notes |
| --- | --- | --- |
| Run / Run Selected enabled while `IsRunning` | **Phase 12** C | Already planned; keep |
| Single overwriteable `Status` string | **Phase 12** A | Severity banner |
| Fail auto-filter with no chip | **Phase 12** C | Already planned; keep |
| Interaction host Continue-only / Cancel = Safety Stop confusion | **Phase 12** D (new) | Clearer copy + distinct abort affordance labels |
| Home is info-only (no CTAs) | **Phase 12** E (new) | Wayfinding buttons into Run / Instruments / Results |
| Inspect / Results weak empty states | **Phase 12** E | One-line empty copy + navigate to Run |
| Session strip missing activity / remaining time | **Phase 11** D | Already planned; keep |
| Settings idle hours vs Phase 11 minutes; mock tooltip soft | **Phase 11** B + **Phase 13** B | Minutes UI in 11; effective-mode honesty in 13 |
| Dense chrome / MinWidth / little AutomationProperties | **Deferred** kiosk + later polish | Not a Phase 11–13 exit gate |
| Hardcoded English | [deferred-localization.md](../deferred/deferred-localization.md) | |

### Lower polish / tech debt

| Finding | Destination | Notes |
| --- | --- | --- |
| Report Preview bitmap leak on clear | **Phase 12** C | Already planned |
| `PostToUi` swallows dispatcher failures | **Phase 12** B | Log + avoid silent wrong-thread fallback |
| Home crash banner bare `catch` | **Phase 12** E → finish in **Phase 15** A | Phase 12 set CrashStatus but it sits inside HasCrashBanner; load failure still invisible — fix visibility in 15 |
| Fat `IOpenTapSession` (~29 members) | **Phase 14** | Done — focused surfaces |
| Run board still heavy after Phase 9 | Optional follow-on | Do not block 15 |
| Disabled Run without reason tip; Status-only pre-run blocks; busy affordances; Instruments/Preview empty; filter selection; theme-hardcoded banners; Settings sticky Save / About redundancy | **Phase 15** | First-impression cluster |
| Stub run comparison TODOs | [deferred-run-comparison.md](../deferred/deferred-run-comparison.md) | |

### Doc / code mismatches

| Finding | Destination | Notes |
| --- | --- | --- |
| README still says platform 1–9 / OpenTAP A–J | Doc hygiene (roadmap sync) | Update layout blurb to 1–14 / A–K |
| `adapting.md` present-tense Phase 11 behavior | **Phase 11** C + immediate clarify | Mark planned vs current; rewrite to past tense when 11 ships |
| Phase 9 “~300 line parent” not met | Note only | Do not reopen Phase 9; optional later |
| Phase 1 still In progress | Unchanged | CI green remains Phase 1 exit |

## What “finish Phase 11” means after this merge

Phase 11 is complete when its expanded exit criteria pass: activity-aware idle, configurable minutes + soft-warn, confirm-every-run, strip countdown, **and** the Same DUT / `RequireOperator` footgun is closed (including Stale UI that can collect a technician when required). See [phase-11-session-activity-stale.md](phase-11-session-activity-stale.md).

## Out of this remediation map

- Implementing OpenTAP Phase K (blocked on 14)
- Appliance image bake / systemd kiosk
- Schema migration engine, remote crash upload, auto-update
- Exhaustive Accessibility tree / AutomationProperties pass (track under deferred kiosk if needed)
