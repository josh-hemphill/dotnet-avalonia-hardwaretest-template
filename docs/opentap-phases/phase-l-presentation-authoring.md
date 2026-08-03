# Phase L — Presentation authoring (band-first maintainability)

**Parent:** [opentap-platform.md](../opentap-platform.md)
**Depends on:** [Phase I](phase-i-presentation-contract.md), [Phase J](phase-j-presentation-ui.md)
**Unblocks:** [Platform Phase 16](../platform-phases/phase-16-band-focus-presentation.md) (Band board + Focus trend)
**Status:** Done

## Goal

Make Presentation **easy to maintain when authoring TapPlans and plugin steps**: authors declare *pass criteria and metric intent* (in/out of band, timing windows, envelope compliance), not shell layout. Prefer derived scalars with limits over raw waveforms whenever the verdict does not require shape judgment. Keep plugins Avalonia-/ScottPlot-free.

## Locked decisions

- **Plans declare intent; the shell chooses layout.** Authors set `DisplayRole` + `ChannelKey` + limits/units. They never name panes, heights, Band/Focus modes, or ScottPlot.
- **Band-first authoring default.** If the pass/fail rule is a threshold, window, or envelope, publish a **`scalar` / `passband`** (or timing-derived scalar) with `LimitLow` / `LimitHigh`. Publish `timeseries` only when operators/engineers need the shape (debug, unknown failure modes, multi-scale inspection).
- **Do not invent UI in plugins.** No new Window/dialog; no gauge geometry in OpenTAP types.
- **Additive roles only.** Keep `timeseries` / `scalar` / `passband`. Add at most one optional role (`timing`) **if** a dedicated timing-bar widget ships in Phase 16; otherwise encode bump/return checks as named scalars (preferred for v1 maintainability).
- **Cookbook over tribal knowledge.** Ship an authoring recipe table in [adapting.md](../adapting.md) (and this phase’s demo matrix) so new benches copy patterns, not reverse-engineer Board Demo.
- **Demos are the contract tests for authors.** Sample / Board / one new or extended demo must exercise band-first recipes; CI ViewModel/host coverage asserts role mapping, not pixels.

## Why this exists

Phase I/J made charts/gauges work, but authoring still defaults to “publish the waveform.” That forces the shell to show squashed plots for checks that are really in/out of band. Platform Phase 16 can fix layout only if authors have a **stable, documented** way to publish the verdict metrics the Band board needs.

## Workstreams

### A — Authoring cookbook (docs + recipes)

Document in `adapting.md` (Presentation section) and link from Phase I:

| Recipe | What to publish | Role | Limits | When to also publish timeseries |
| --- | --- | --- | --- | --- |
| Rail / mean in band | `rail.X.mean` (or equivalent) final value | `passband` | LimitLow / LimitHigh = spec | Acquire series optional for Focus/debug |
| Scalar threshold (GTE/LTE) | Mean / computed value | `scalar` or `passband` | One-sided or two-sided limits | Only if shape matters |
| Bump / pulse timing | Derived: `bump.rise.ms`, `bump.width.ms`, `bump.peak` | `scalar` / `passband` | Window bounds as limits | Raw series only for Focus |
| Hi → Low return | Derived: `return.high.at.ms`, `return.low.at.ms`, `return.ok` (0/1) or worst excursion | `scalar` / `passband` | Timing + amplitude limits | Raw series only for Focus |
| Envelope / return bounds | Derived: `envelope.error` / `overshoot` / `undershoot` | `passband` | Spec envelope | Raw series for Focus |

Rules of thumb for authors:

1. Write the pass criteria in words first (“rise between 5–15 ms, return low by 50 ms”).
2. Publish **one Scalar row per criterion** with limits.
3. Attach Presentation mixin `ChannelKey` stable across plan revisions.
4. Add `timeseries` on the acquire step only when Focus trend is useful — not as the sole pass signal.

### B — Contract clarity (code + mixin copy)

- Tighten `PresentationMixin` Display descriptions to point authors at band-first defaults (`passband`/`scalar` for verdicts; `timeseries` for shape).
- Keep `PresentationDisplayRoles` as the single source of allowed role strings; unknown → text-only (existing Phase J rule).
- If Phase 16 ships a timing-bar widget, add optional `timing` to `PresentationDisplayRoles` + `PresentationRoleMap` in the **same** change set as the widget (do not leave a dangling role).
- Host normalization stays in `OpenTapPresentation`; no Avalonia types in plugins.

### C — Demo / fixture coverage for authoring

- Extend Board Demo (or add a small **Timing Demo** factory) so at least one path publishes **derived timing/envelope scalars with limits**, not only acquire+mean.
- Keep Sample Demo as the minimal band + optional series teaching example.
- Document the matrix in this file (same style as Phase I demo matrix).
- ViewModel / role-map tests: band metrics upsert gauges; timeseries still feeds the plot path; unknown roles degrade.

### D — Results & reports stay consistent

- Results Metrics strip follows the same role map (gauges for scalar/passband; charts for timeseries).
- Typst / `EmbedPlotsInReport`: timeseries may embed; band verdicts remain readable as value+limits in the report tables even when plots are off.
- Do not require authors to special-case report layout.

## Authoring anti-patterns (explicit)

- Publishing only a long `Sample` series and expecting operators to “see” pass/fail on a 140px chart.
- Encoding colors, brush hex, or widget names in plan metadata.
- New `DisplayRole` strings per bench (“gauge-big”, “plot-main”) — extend the shared enum/list instead.
- Duplicating the same mean as both unlabeled Sample and Scalar without `ChannelKey` stability.


### Demo matrix

| Plan | Step | ChannelKey | DisplayRole | YUnit |
| --- | --- | --- | --- | --- |
| Timing | Simulate bump waveform | `bump.v` | timeseries | V |
| Timing | Bump rise time | `bump.rise.ms` | passband | ms |
| Timing | Return low time | `return.low.at.ms` | passband | ms |
| Timing | Envelope error | `envelope.error` | passband | V |

## Exit criteria

- [x] `adapting.md` Presentation cookbook with the recipes above (band / bump / return / envelope)
- [x] Mixin Display text steers authors to band-first verdicts
- [x] At least one demo publishes derived timing or envelope scalars with limits
- [x] Role map + host tests cover scalar/passband/timeseries; unknown roles still degrade
- [x] Phase 16 can consume existing roles without requiring every live metric to be a waveform

## Out of scope

- Shell Band/Focus layout (Phase 16)
- Multi-DUT (Phase K)
- ML baselines / golden-curve correlation UI
- Separate analytics application

## Related

- [Phase I](phase-i-presentation-contract.md) — publish tables + mixin
- [Phase J](phase-j-presentation-ui.md) — role → widget map
- [Phase 16](../platform-phases/phase-16-band-focus-presentation.md) — Band board + earned Focus trend
