# Phase 16 — Band board & Focus trend

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 15](phase-15-operator-feedback-chrome.md), [Phase J](../opentap-phases/phase-j-presentation-ui.md), [Phase L](../opentap-phases/phase-l-presentation-authoring.md) (authoring cookbook + demos; may land same PR train)
**Status:** Done
**Also absorbs:** Live chart squash (fixed 140px plot in MaxHeight-360 tray; Results 120px charts in 360px sidebar); band-first operator chrome with earned trend

## Goal

Make live measurement feedback **glanceable and maintainable**: default Run chrome is an in/out-of-band KPI strip; a full trend appears only when it earns the space (selection, out-of-band, or explicit expand) and then gets honest height via a real splitter pane—not a squashed ornament. Keep Presentation authoring (Phase L) as the source of truth so new tests stay band-first without shell rewrites.

## Locked decisions

- **Band-first, earned trend (middle ground of layout promotion + gauge-first).** Default = KPI / passband strip. Focus trend pane is optional and promoted—not an always-visible 140px chart.
- **Two modes only for v1:** `Band` and `Focus`. No separate Analyze mode until benches prove need.
- **Shell owns layout; plans own intent.** Mode switching never requires TapPlan changes beyond Phase L roles/limits.
- **No new OS dialogs / second windows** — Focus stays in-panel ([appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned)). No separate Monitor page in this phase.
- **Constraint trigger is usable plot area + operator/alert intent**, not raw window width alone ([constraint-based breakpoints](https://arxiv.org/html/2409.01339) idea, applied thinly).
- **When Focus is shown, drop fixed `Height="140"` / Results `Height="120"` for that surface** — use stretch + `MinHeight` floor (~160–180px Operate). Focus lives **inside** the Details drawer as a nested `*` row (0 when closed); gauges stay in a sibling `*` row with a `ScrollViewer` so the band pane remains scrollable. Step list and Details share one outer splitter; chrome toggles reset star rows so Absolute heights from drag cannot stick.
- **Sparklines are optional cues under KPIs**, not a substitute for Focus when shape diagnosis is required.
- **Bump / hi-low / return** prefer Phase L derived scalars + limits (timing bar widget optional). Full waveform remains the Focus path.
- Keep Feature line budgets (~600); extend `LivePresentationViewModel` / plot host via partials rather than raising the cap.

## Workstreams

### A — Run board layout (honest Focus pane)

- Restructure the Details tray so **gauges are not competing with a fixed-height plot inside `MaxHeight="360"`**.
- **Band mode:** selected-step (or active) KPI strip visible; plot collapsed or sparkline-only.
- **Focus mode:** KPI strip retained; **one** `MeasurementPlotView` stretches in a nested star row inside Details (`MinHeight` floor, no fixed Height); gauges/detail scroll in the sibling star row. Operator resizes list vs drawer via the outer splitter.
- Enter Focus when: operator selects a timeseries-bearing step/metric, a watched gauge goes out of band, or “Show trend” is toggled. Exit Focus restores Band without destroying sample buffers.
- Preserve session / interaction / Safety Stop hierarchy; do not bury transport.

### B — Mode policy (thin constraints)

- Implement `PresentationChromeMode` = `Band` | `Focus` on `LivePresentationViewModel` (or adjacent host API).
- Inputs: available height for plot region, whether selected metric has timeseries samples, out-of-band flag on selected/visible gauges, explicit user expand.
- If Focus cannot meet `MinHeight` floor, keep Band and surface a one-line Status/tip (“Widen Details / expand trend”)—do not crush marks below the floor (constraint-breakpoint spirit).

### C — Results Metrics (same language)

- Stop stacking many tiny charts in the 360px scrolling gutter as the only view.
- Prefer gauges for scalar/passband; for timeseries show **one focus chart** (or paginated) with a min height, or navigate/expand pattern consistent with Run.
- Align with Phase L: band verdicts readable without opening a waveform.

### D — Optional timing / envelope chrome

- **v1 preferred:** no new widget—Phase L scalars + existing `MetricGaugeView` / passband band.
- **v1.1 if demos need it:** compact timing-window bar (event marks + windows) and/or hi→low state strip, mapped from optional `timing` role or from well-known metric key suffixes documented in Phase L.
- Do not ship a dangling `timing` role without a widget.

### E — Tests & authoring handshake

- ViewModel tests: mode transitions (Band↔Focus), out-of-band auto-promote, collapse when no series.
- Binding/layout smoke: Focus plot not bound to fixed 140 when mode is Focus (assert via VM flags + AXAML contract tests where practical).
- Manual eval uses Phase L demo matrix (Sample mean gauge; Board passband; timing/envelope demo if present).
- Update [adapting.md](../adapting.md) cross-link: cookbook (L) + what operators see (16).

## Exit criteria

- [x] Run default chrome is Band (KPI strip); no always-on squashed primary plot
- [x] Focus trend uses stretch + MinHeight + splitter; fixed 140px primary path removed for Focus
- [x] Focus opens on selection / out-of-band / explicit expand; returns to Band cleanly
- [x] Results Metrics prefer band gauges; timeseries not forced into unreadably short stacks
- [x] Phase L cookbook recipes work end-to-end on demos without plan-side layout hacks
- [x] Feature line budgets + ViewModels / architecture / host suites green

## Out of scope

- Dedicated Live Monitor navigation page (revisit if Focus still cramped on real benches)
- Full multi-scale banking automation / Analyze mode
- Multi-DUT (OpenTAP Phase K)
- Rewriting Typst chart geometry wholesale
- Localization / kiosk density pass

## Related

- Design discussion: band-first + earned trend; bounds without full waveform (bump/return → derived indicators)
- [Phase L](../opentap-phases/phase-l-presentation-authoring.md) — authoring contract & cookbook
- [Phase J](../opentap-phases/phase-j-presentation-ui.md) — existing role → widget map
- [Phase 9](phase-9-runboard-decomposition.md) — `LivePresentationViewModel` ownership
- Research anchors: Cleveland/McGill graphical perception; banking to 45°; Schöttler et al. constraint-based breakpoints (IEEE TVCG 2024); ISA-101 / High Performance HMI trend embedding
