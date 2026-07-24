# Phase J — Presentation UI

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase I](phase-i-presentation-contract.md), loop-stamped samples, [DutHistoryService](../../src/HardwareTest.Core/Runs/DutHistoryService.cs)  
**Unblocks:** Richer operator visualization without a separate analytics app  
**Status:** Planned

## Goal

Map presentation roles from Phase I to shell widgets: live gauges, multi-series / threshold overlays on Run, and richer Results charts — still Avalonia-owned, still appliance-safe (no floating OpenTAP dialogs).

## Locked rules

- Avalonia owns all presentation chrome.
- Live Run plot may stay chronological; role-based tiles are additive.
- History anomaly text already exists; this phase adds visual sparklines / gauges only where roles demand them.

## Work items

1. **Role → widget map** — `timeseries` → series plot; `scalar` → gauge/KPI; `passband` → value + limit band.
2. **Run board** — optional tiles driven by active step / published scalars (not a dashboard dump).
3. **Results** — richer channel charts using iteration stamps + presentation keys.
4. **Tests** — ViewModel mapping tests with Fake publishes; smoke that Run remains usable without presentation mixin.

## Exit criteria

- Demo plan with Presentation mixin shows at least one gauge and one series without plugin UI code.
- Unknown roles degrade gracefully (ignore / show as text).

## Out of scope

- Separate companion analytics application.
- Remote MES dashboards (CSV export remains the handoff).
