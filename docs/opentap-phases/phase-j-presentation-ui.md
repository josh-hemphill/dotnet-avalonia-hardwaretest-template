# Phase J — Presentation UI

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase I](phase-i-presentation-contract.md), loop-stamped samples, [DutHistoryService](../../src/HardwareTest.Core/Runs/DutHistoryService.cs)  
**Unblocks:** Richer operator visualization without a separate analytics app  
**Status:** Done

## Goal

Map presentation roles from Phase I to shell widgets: live gauges, multi-series / threshold overlays on Run, and richer Results charts — still Avalonia-owned, still appliance-safe (no floating OpenTAP dialogs).

## Locked rules

- Avalonia owns all presentation chrome.
- Live Run plot may stay chronological; role-based tiles are additive.
- History anomaly text already exists; this phase adds visual sparklines / gauges only where roles demand them.

## Implementation

1. **Role → widget map** — [`PresentationRoleMap`](../../src/HardwareTest/Features/Presentation/PresentationRoleMap.cs): `timeseries` → live/`MeasurementPlotView`; `scalar` / `passband` → [`MetricGaugeView`](../../src/HardwareTest/Widgets/Presentation/MetricGaugeView.cs); unknown → text only.
2. **Progress enrichment** — `MeasurementSampleEvent` carries MetricKey / DisplayRole / Unit / LimitLow / LimitHigh. Scalar publishes include optional `LimitLow`/`LimitHigh` columns (`MeanGteStep` publishes Threshold as LimitLow).
3. **Run board** — Selected-step gauge tiles (`PresentationTiles`); chronological plot kept for timeseries Sample publishes with MetricKey/Unit labels.
4. **Results** — Per-`EffectiveMetricKey` chart/gauge strip above sample text; [`SamplePlotExporter`](../../src/HardwareTest.Core/Reporting/SamplePlotExporter.cs) groups by EffectiveMetricKey.

### Manual eval checklist

1. Run Sample → select Mean GTE → gauge tile for `VDC.mean`; select Acquire → live series plot.
2. Run Board Demo → Mean GTE 3V3 / Bus show passband gauges with LimitLow band.
3. Results → open run → Metrics section shows timeseries chart + scalar/passband gauges.
4. Fake/no-mixin path: plot + KeyValue still work with zero gauge tiles.

## Exit criteria

- Demo plan with Presentation mixin shows at least one gauge and one series without plugin UI code.
- Unknown roles degrade gracefully (ignore / show as text).

## Out of scope

- Separate companion analytics application.
- Remote MES dashboards (CSV export remains the handoff).
