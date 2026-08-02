# Phase 14 — Session façade split

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 8](phase-8-session-contract-tests.md), [Phase 9](phase-9-runboard-decomposition.md)
**Unblocks:** OpenTAP [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT)
**Status:** Planned

## Goal

Split the ~29-member [`IOpenTapSession`](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs) god interface into focused surfaces so multi-DUT / parallel work does not multiply one mega-session, while keeping Phase 8 contract tests green.

## Locked decisions

- **Behavioral compatibility first.** Fake + real contract suites ([Phase 8](phase-8-session-contract-tests.md)) must keep passing; prefer adapter façades over a big-bang rewrite.
- Suggested seams (names illustrative):
  - **Plan load / tree** — load sample/board/sweep/file, step tree, loaded plan metadata
  - **Run control** — run / selection / pause / resume / abort / progress / interaction
  - **Station bind** — slots, ApplyStationAndDut, parameters, mixins bridge
  - **Discovery / packages** — device addresses, package list, plugin dirs
- UI may still receive a **composition root** that implements a thin `IOpenTapSession` aggregating the parts (backward compatible) **or** take the focused interfaces via DI — pick one and stick to it in Composition.
- No Avalonia types in Host. No new operator dialogs.
- Do **not** implement multi-DUT execution in this phase — only the seams Phase K needs.

## Workstreams

### A — Interface extraction

- Extract interfaces from current `IOpenTapSession` members without moving logic first.
- `OpenTapSession` implements all; register focused interfaces as the same singleton instance initially.

### B — Consumer migration

- Update Run board, Instruments, Settings, Inspect, Composition to depend on the narrowest interface each needs.
- Keep a deprecated aggregating `IOpenTapSession` until Phase K lands if that reduces churn.

### C — Contracts

- Retarget or duplicate Phase 8 tests per surface where valuable; keep at least one “full session” runner for regression.
- FakeOpenTapSession implements the same split.

## Exit criteria

- [ ] Focused interfaces exist; Composition registers them
- [ ] Feature ViewModels no longer need every session member to compile
- [ ] Phase 8 real + fake contract suites green
- [ ] Architecture tests still enforce Host Avalonia-free / Core OpenTAP-free
- [ ] Phase K can consume run + session-identity seams without the packages API

## Out of scope

- Multiple concurrent OpenTAP plan executions (Phase K.2)
- Rewriting parameter/mixin bridges
- Remote Agent / REST
