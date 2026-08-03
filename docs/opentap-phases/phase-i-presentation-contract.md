# Phase I — Presentation contract

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** Result publish path ([ProgressResultListener](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs)), loop-stamped samples (`StoredSample.IterationIndex`)  
**Unblocks:** [Phase J](phase-j-presentation-ui.md) gauges / role-based Run widgets  
**Status:** Done

## Goal

Let TapPlans declare **what** to show (metric identity + display role) without Avalonia, ScottPlot, or colors in plugins. The shell maps roles to widgets later.

## Locked rules

- No Avalonia / ScottPlot types in OpenTAP plugins.
- Golden `.TapPlan` files stay Editor-authored; presentation is conventions + optional mixin.
- Typst remains the operator PDF path; this phase does not replace reports.

## Implementation

1. **Publish conventions**

   | Table | Columns | Intent |
   | --- | --- | --- |
   | `Sample` | Channel, Index, Value | timeseries |
   | `Scalar` | Name, Value, Unit, optional LimitLow / LimitHigh | scalar / passband metric |
   | `Identity` / `Analyze` | unchanged | chrome; Mean also published as Scalar |

2. **Presentation mixin** — [`PresentationMixin`](../../src/HardwareTest.OpenTap.Plugins.Mixins/PresentationMixin.cs) / [`PresentationMixinBuilder`](../../src/HardwareTest.OpenTap.Plugins.Mixins/PresentationMixinBuilder.cs): `ChannelKey`, `DisplayRole` (`timeseries` / `scalar` / `passband`), `YUnit`, optional `HistoryEnabled` / `HistoryWatchPercent` / `HistoryAlertPercent`. Attach via Editor or [`OpenTapMixinAttach.AttachPresentation`](../../src/HardwareTest.OpenTap.Host/OpenTapMixinAttach.cs).

3. **Host normalization** — [`OpenTapPresentation`](../../src/HardwareTest.OpenTap.Host/OpenTapPresentation.cs) fills `StoredSample.MetricKey` / `DisplayRole` / `Unit` / history fields. DUT history groups by `EffectiveMetricKey` and uses stamped thresholds when present.

4. **Visibility (pre–Phase J)** — Results sample lines use `ToDisplayLine()`; Run detail lists Presentation station fields; Engineer overrides show Presentation group.

### Demo matrix

| Plan | Step | ChannelKey | DisplayRole | YUnit |
| --- | --- | --- | --- | --- |
| Sample | Acquire VDC | `VDC` | timeseries | V |
| Sample | Mean GTE | `VDC.mean` | scalar | V |
| Board | Acquire 3V3 | `rail.3v3` | timeseries | V |
| Board | Mean GTE 3V3 | `rail.3v3.mean` | passband | V |
| Board | Acquire 5V | `rail.5v` | timeseries | V |
| Board | Mean GTE 5V | `rail.5v.mean` | scalar | V |
| Board | Long Acquire VDC | `bus.vdc` | timeseries | V |
| Board | Mean GTE Bus | `bus.vdc.mean` | passband | V |
| Sweep | Acquire VDC (loop) | `sweep.vdc` | timeseries | V |

### Manual eval checklist

1. Run Sample → Results → samples show `VDC [timeseries]` and `VDC.mean [scalar]`.
2. Run Board Demo → Results → `rail.*` / `bus.*` with scalar and passband roles.
3. Run Sweep Demo → iteration stamps + `sweep.vdc`.
4. Engineer mode → Station overrides on Acquire 3V3 → Presentation group; change YUnit, re-run → Unit in Results.

## Exit criteria

- Documented contract + mixin stub testable without UI.
- Plans can express chart/gauge *intent* without referencing shell controls.

## Out of scope

- Live gauges / multi-series Run board (Phase J).
- Editable baselines UI / ML predictions.
