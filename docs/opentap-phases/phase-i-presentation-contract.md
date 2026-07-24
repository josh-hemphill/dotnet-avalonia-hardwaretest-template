# Phase I — Presentation contract

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** Result publish path ([ProgressResultListener](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs)), loop-stamped samples (`StoredSample.IterationIndex`)  
**Unblocks:** [Phase J](phase-j-presentation-ui.md) gauges / role-based Run widgets  
**Status:** Planned

## Goal

Let TapPlans declare **what** to show (metric identity + display role) without Avalonia, ScottPlot, or colors in plugins. The shell maps roles to widgets later.

## Locked rules

- No Avalonia / ScottPlot types in OpenTAP plugins.
- Golden `.TapPlan` files stay Editor-authored; presentation is conventions + optional mixin.
- Typst remains the operator PDF path; this phase does not replace reports.

## Work items

1. **Publish conventions** — document stable table/column shapes beyond today’s `Sample` / `Identity` / `Analyze` (e.g. `Scalar` with `Name,Value,Unit`; optional limit columns).
2. **Presentation mixin** — `ChannelKey`, `DisplayRole` (`timeseries` / `scalar` / `passband`), `YUnit` attachable in Editor; enumerable via Phase C parameter bridge.
3. **Host normalization** — map published tables + mixin hints to stable metric keys for `StoredSample` / history / future UI.
4. **Tests** — mixin members round-trip via `TrySetParameter`; convention fixtures publish expected tables.

## Exit criteria

- Documented contract + mixin stub testable without UI.
- Plans can express chart/gauge *intent* without referencing shell controls.

## Out of scope

- Live gauges / multi-series Run board (Phase J).
- Editable baselines UI / ML predictions.
