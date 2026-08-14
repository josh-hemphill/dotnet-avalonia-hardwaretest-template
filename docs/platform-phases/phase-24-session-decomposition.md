# Phase 24 — OpenTAP session decomposition

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 14](phase-14-session-facade-split.md) (public `IOpenTap*` surfaces already exist)
**Unblocks:** [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT must not share one 1738-line god object)
**Status:** Planned
**Also absorbs:** Round-3 R3-15 (`OpenTapSession` size, `StepRuntime` statics, serial host tests)

## Goal

Keep the **public** `IOpenTapPlanSession` / `IOpenTapRunSession` / `IOpenTapStationSession` / `IOpenTapHostCatalog` surfaces and split the **implementation** so run state is an `OpenTapRunContext`, plugins are not process-global statics, and host tests can isolate. This is structure, not new operator features.

## Why this exists

Phase 14 split the **injection** surface so Feature ViewModels no longer take the aggregating `IOpenTapSession`. The implementation is still one type (~1738 LOC) plus a ~1687 LOC fake that duplicates it. `StepRuntime` exposes static `WaitIfPaused` / `RequestInteraction`. Host tests must be `[Collection("OpenTapSerial")]` because TapThread / PluginManager are process-global. Coverlet on that suite flakes. Phase K cannot compose two DUTs on this object.

## Locked decisions

- **Do not change** the public `IOpenTap*` method sets in this phase except to add context-aware overloads that default to today’s singleton behavior. Update Session.Contracts approved snapshots in the same commit if signatures move.
- Replace `StepRuntime` statics with **`IRuntimeAwareStep`** (or constructor-injected runtime) so pause/interaction is per run context.
- **`OpenTapRunContext`** owns CTS, pause gate, samples, step results, interaction for one execute. `OpenTapSession` (or a thinner host) creates/disposes contexts.
- Fake session should **compose the same helpers** where practical rather than fork another 1600 lines. Prefer shared test doubles for progress/interaction.
- Process isolation of tests is **Phase 23’s worker** or a dedicated testhost; this phase at least removes static run state so two contexts can exist in one process for unit tests.
- Feature 600-line cap still applies to UI; Host types should split rather than grow.

## Workstreams

### A — Run context

- Extract execute/pause/abort/progress ingest into `OpenTapRunContext`.
- Session methods become thin delegates to the active context; reject overlapping execute (already gated).

### B — Kill `StepRuntime` statics

- Steps receive `IStepRuntime` from the host when the plan is built or via a well-known OpenTAP service.
- Tests inject a fake runtime; no `WaitIfPaused` static.

### C — Catalog vs session

- Plugin search / package catalog / device discovery already sit behind `IOpenTapHostCatalog`. Move remaining catalog fields out of the run type.

### D — Fake + contracts

- Shrink `FakeOpenTapSession` by sharing context/progress fakes.
- Phase 8 contract suite still green; update approved files only when surfaces change.

## Exit criteria

- [ ] `OpenTapSession` is a façade over context + catalog + station, not a god object
- [ ] No static pause/interaction on `StepRuntime`
- [ ] Two run contexts can exist in-process in a unit test (even if production still single-flights)
- [ ] Session.Contracts snapshots match the public surfaces
- [ ] ViewModels unchanged except DI if a new constructor appears
- [ ] Host tests no longer require static `StepRuntime` coupling (serial collection may remain until Phase 23)

## Out of scope

- Multi-DUT scheduling (Phase K)
- Worker IPC (Phase 23) — consume the thinner types; do not block K’s design on IPC details
- UI Run board decomposition (already Phase 9)

## Related

- [phase-14-session-facade-split.md](phase-14-session-facade-split.md)
- [phase-8-session-contract-tests.md](phase-8-session-contract-tests.md)
