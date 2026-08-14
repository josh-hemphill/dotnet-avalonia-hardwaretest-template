# Deferred — Run comparison

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related code:** [`StubRunComparisonService`](../../src/HardwareTest.Core/Runs/FileRunStore.cs), `IRunComparisonService`

## Goal

Replace the stub run-to-run comparison service with a real, operator-useful comparison of persisted test runs (same plan / DUT / channel metrics).

## Locked decisions

- Comparison is **local** (on-disk `runs/`); no cloud sync in this plan.
- Prefer Presentation `MetricKey` grouping when present (same rules as DUT history).
- UI stays in-panel on Results (side-by-side or delta table) — no second Window.
- Do not block Run on comparison failures.
- Do **not** register `StubRunComparisonService` as an operator-facing Results feature until this plan is implemented. Keep the stub out of operator chrome.

## Workstreams

1. Implement `IRunComparisonService` against `IRunStore` (select baseline + candidate run ids).
2. Metric alignment: Channel / MetricKey, units, passband limits when available.
3. Results UI: pick two runs (or “compare to previous”), show deltas + severity.
4. Tests: fixture pair of `TestRunRecord`s with known deltas.

## Exit criteria

- [ ] Stub removed; DI registers a real implementation
- [ ] Operator can compare two completed runs and see per-metric deltas
- [ ] Missing metrics degrade gracefully (listed as unavailable, not crash)

## Out of scope

- Statistical process control charts beyond simple deltas
- Automatic gate fail on drift (product policy later)
- Cross-station comparison

## Dependencies

- Stable Presentation contract (Phases I/J — Done)
- DUT history service patterns ([`DutHistoryService`](../../src/HardwareTest.Core/Runs/DutHistoryService.cs))
