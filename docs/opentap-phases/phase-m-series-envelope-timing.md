# Phase M — Series envelope + timing chrome

**Parent:** [opentap-platform.md](../opentap-platform.md)
**Depends on:** [Phase I](phase-i-presentation-contract.md), [Phase L](phase-l-presentation-authoring.md), platform [Phase 16](../platform-phases/phase-16-band-focus-presentation.md)
**Unblocks:** bit-sweep / GPIB config tests that must stay in band for the whole acquire; clearer elapsed-time and event marks
**Status:** Planned

## Goal

Let a plan **measure a value over time while configuration bits change**, **fail if any sample leaves the limit band**, and show **when** those bits changed — without a new Analyze mode, without parallel OpenTAP, and without putting Avalonia types in plugins.

Today that story is split and incomplete:

- `timeseries` is shape-only. `AcquireVoltageStep` always Passes. Limits live on a *later* Scalar, so the live plot often has no band.
- Sample timestamps are stamped in [`OpenTapProgressResultListener`](../../src/HardwareTest.OpenTap.Host/OpenTapProgressResultListener.cs) at ingest (`IClock.UtcNow`), not by the step. Coalesced Sample frames and host delay make “when” fuzzy.
- Bit / config changes have no publish table. Sweep chrome is `iter i/N` only; Results gauges collapse to the last `EffectiveMetricKey`.
- Phase 16 left a timing-bar widget as v1.1 and [Phase L](phase-l-presentation-authoring.md) forbids a dangling `timing` role.

## Locked decisions

- **Plans own verdict.** The shell never invents Pass/Fail from the plot. A step (acquire or analyze) upgrades Fail when series compliance is on and a sample (or dwell) is out of band.
- **One step owns the stream.** OpenTAP stays sequential (parallel execute is [Phase K.2](phase-k-multi-dut-parallel.md)). The function step that writes GPIB/config bits also publishes `Sample` + `Event` on one elapsed clock. Do not require a sibling acquire running in parallel.
- **Plan-owned time.** `Sample` and `Event` may publish `ElapsedMs` (double, from step start). Host uses it when present; otherwise keep today’s ingest timestamp (legacy). Do not treat host receive time as the timing source of truth for new plans.
- **Series compliance is a setting, not a DisplayRole.** Keep `timeseries` / `scalar` / `passband`. Add `SeriesCompliance` (`none` | `allSamples` | `dwell`) on the acquire/analyze step (and optionally a Presentation mixin hint for chrome only). `allSamples` = every published sample must sit in `[LimitLow, LimitHigh]` (inclusive, same as library scalars). `dwell` = also fail when a contiguous out-of-band run exceeds `DwellLimitMs`.
- **`timing` role ships with the widget.** Add `PresentationDisplayRoles.Timing` and `PresentationTileKind.Timing` in the **same** change set as [`TimingStripView`](../../src/HardwareTest/Widgets/Presentation/) (Phase L rule). Unknown roles still degrade to text.
- **Events are a table, not a role.** New Phase I table `Event`: `Name`, `ElapsedMs`, `Label`, optional `Value` (config word / bit index). Host stores `StoredEvent` on the run record.
- **Band stays default.** Focus/Chart earns space when the selected step has a series (existing Phase 16). The timing strip sits **with** the chart (under the plot, Measurements pane) — not as a fourth Run workspace.
- **No bit-field editor.** Encode config identity in `Event.Label` / `ChannelKey` (`cfg.bit3.vout`). Avalonia does not edit GPIB strings or register maps ([deferred-instrument-pack-binding.md](../deferred/deferred-instrument-pack-binding.md)).
- **No new OS dialogs.** Markers and strips stay in-panel.

## Authoring recipe (cookbook addition)

| Recipe | What to publish | Role | Limits / events | Verdict |
| --- | --- | --- | --- | --- |
| Series stays in band | `Sample` stream + Scalar `series.inband.pct` / `series.excursion.max` | acquire = `timeseries`; summaries = `passband` | `LimitLow`/`LimitHigh` on **Sample** rows (and mixin/step) | Step Fail if `SeriesCompliance=allSamples` and any point is out |
| Bit / GPIB config while measuring | Same stream + `Event` at each write | acquire = `timeseries`; optional sibling `timing` for the strip | Event `Label` = bit/word; `ElapsedMs` aligned to Sample | Same; events do not themselves Pass/Fail |
| Window / dwell | Derived Scalar `series.outband.ms` + optional `timing` windows | `passband` or `timing` | `DwellLimitMs` / window `[t0, t1]` | Fail on dwell if mode is `dwell` |
| Shape-only (today) | `Sample` without limits | `timeseries` | none | Acquire Pass; use a later Scalar if needed |

Rules of thumb:

1. Write the pass sentence first (“Vout stays in 3.2–3.5 V for the whole bit walk; no out-of-band dwell > 2 ms”).
2. Put limits on the **series**, not only on a post-hoc mean.
3. Publish one `Event` per config write on the same `ElapsedMs` clock as `Sample`.
4. Keep `ChannelKey` stable (`rail.3v3` for the series; `rail.3v3.inband.pct` for the summary).

## Workstreams

### A — Publish contract + host normalization

1. Extend Phase I `Sample` columns: `Channel`, `Index`, `Value`, optional `LimitLow`, `LimitHigh`, `ElapsedMs`. Existing three-column publishes keep working.
2. Add `Event` table as above. Listener ignores unknown tables (already).
3. [`OpenTapPresentation.ApplySample`](../../src/HardwareTest.OpenTap.Host/OpenTapPresentation.cs): copy published limits and `ElapsedMs` onto `StoredSample`. If columns are absent, do not invent limits from a later Scalar.
4. [`MeasurementSampleEvent`](../../src/HardwareTest.OpenTap.Host/OpenTapModels.cs) carries `ElapsedMs`. [`LiveSeriesBuffer.Append`](../../src/HardwareTest/Features/RunTest/LiveSeries.cs) prefers `ElapsedMs / 1000` over timestamp delta when set.
5. New `StoredEvent` on [`TestRunRecord`](../../src/HardwareTest.Core/Runs/TestRunModels.cs): `Name`, `ElapsedMs`, `Label`, `Value`, `StepPath`, `Timestamp`. Bump [`SchemaVersions.TestRunRecord`](../../src/HardwareTest.Core/Serialization/SchemaVersions.cs) **2 → 3**. Absent `Events` / `ElapsedMs` = legacy (no markers; buffer uses ingest time).
6. Progress: optional `MeasurementEventMark` (or reuse `OpenTapProgress` with `Event` payload) so the UI can draw marks live without waiting for run-complete.

### B — Steps (Basic demo + product path)

1. **`AcquireVoltageStep` (demo only):** optional `LimitLow` / `LimitHigh`, `SeriesCompliance`, `DwellLimitMs`, `FailWhenOutOfBand` (default false to keep Sample/Board green). When compliance is on, Fail on first violating sample (log index + elapsed + limits) and still finish publishing so Chart shows the excursion.
2. **`PublishSeriesComplianceStep` (Analyze):** reads the last Sample series for a channel (or explicit in-memory values in the demo factory) and publishes:
   - `series.inband.pct` (0–100, `LimitLow=100` or `LimitHigh` unused)
   - `series.excursion.max` (worst distance outside the band, `LimitHigh=0`)
   - `series.outband.ms` (contiguous out-of-band time)
   Prefer this when the acquire step is a third-party type that cannot take new properties.
3. **`BitSweepAcquireStep` (demo factory):** mock “config bits” (no real GPIB). For `i in 0..N-1`: record Event `bit{i}` / word value, read voltage, publish Sample with shared limits + `ElapsedMs`. Product plans replace the mock write with an InstrumentComponents / custom GPIB write **inside the same step type** (or a product composite). Do not add a generic SCPI-string UI.
4. Station overrides: limits, compliance mode, sweep count, interval — Phase C bridge, Engineer/Debug only.
5. `PlanContractValidator`: `timeseries` + `SeriesCompliance != none` without any limit warns (same spirit as passband-without-limits).

### C — Timing chrome (shell)

1. **Chart:** keep limit fill + horizontal lines ([`MeasurementPlotView`](../../src/HardwareTest/Widgets/MeasurementPlot/MeasurementPlotView.cs)). Add vertical event markers (label on hover / legend, not a clutter of always-on text). Shade contiguous out-of-band X spans when limits are present.
2. **`TimingStripView`:** 1-D elapsed axis, event ticks, optional window bars from `timing` tiles. No Y scale. Place under the Chart workspace plot and on Results when events or `timing` tiles exist.
3. **Role map:** `timing` → timing strip (not a gauge, not a waveform). `timeseries` still drives Chart. Compliance Scalars still drive Band gauges (in-band %, excursion).
4. **Live copy:** Chart band text already says Within / Out of band. Add elapsed (`t=12.4 ms`) and current event label (`bit3=1`) on the Chart toolbar / Measurements pane — not a growing Run hero row (Phase 17).
5. **Results:** event list under the focus chart; Typst tables list Events + compliance Scalars. Do not require a new PDF kind.
6. Feature line budget: extend `LivePresentationViewModel` via a partial (`LivePresentationViewModel.Events.cs`) rather than raising the 600-line cap.

### D — Demo + tests + docs

- New factory **envelope-sweep-demo** (or extend Timing): mock bit walk, series limits, one intentional excursion with `FailWhenOutOfBand=false` for chrome teaching, plus a compliance Scalar that still shows out of band. Keep Sample/Board unchanged.
- Host: Sample limits + `ElapsedMs` round-trip; Event stored; `allSamples` Fail; legacy three-column Sample still Passes.
- ViewModels: event marks appear; out-of-band span sets Chart attention; `timing` maps to strip; unknown role still text; Focus still earned.
- Architecture: no Avalonia types in plugins; no new Window.
- Update [adapting.md](../adapting.md) cookbook + Phase I demo matrix. Mention `Event` in [testing.md](../testing.md) cassette notes if progress payload grows.

## Exit criteria

- [ ] A demo step can publish a limited `Sample` series plus `Event` marks on a shared `ElapsedMs` clock
- [ ] `SeriesCompliance=allSamples` fails the step when any sample is outside inclusive limits; Chart shows the excursion and event marks
- [ ] Host prefers plan `ElapsedMs` over ingest time when present
- [ ] `timing` role and `TimingStripView` ship together; unknown roles still degrade
- [ ] TestRunRecord schema 3; legacy runs load without markers
- [ ] Cookbook + validator warning for compliance-without-limits
- [ ] Host / ViewModel / architecture suites green; Feature files stay under the line budget

## Out of scope

- Parallel acquire vs write (Phase K.2)
- Avalonia SCPI / register editor
- ML / golden-curve correlation
- Separate Live Monitor page
- Host-side “skip this step and reuse yesterday’s series”
- Changing Sample/Board default verdicts

## Related

- [Phase L](phase-l-presentation-authoring.md) — band-first cookbook; `timing` only with a widget
- [Phase 16](../platform-phases/phase-16-band-focus-presentation.md) — Band + earned Chart; deferred timing bar
- [Phase G](phase-g-sweeps.md) — `iter i/N` remains for Repeat/Sweep; Events are the bit-change story
- [Phase 26](../platform-phases/phase-26-station-health-gating.md) — station health / optional Run gate (independent)
