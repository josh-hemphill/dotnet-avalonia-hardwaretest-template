# Phase 9 — Run board decomposition

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 8](phase-8-session-contract-tests.md) (a safety net under the largest refactor in the plan)
**Unblocks:** further modularization elsewhere, using whatever pattern this phase proves
**Status:** Not started

## Goal

Break [`RunTestViewModel`](../../src/HardwareTest/Features/RunTest/RunTestViewModel.cs) into composed child ViewModels along the responsibility lines it already has, **without** rewriting behavior and without invalidating the 60 tests that cover it.

## Current shape

| Metric | Value |
| --- | --- |
| Lines | 2,675 (85% of the whole `Features/RunTest` folder) |
| `[Reactive]` members | 91 |
| Commands | ~26 |
| `ObservableCollection` properties | 13 |
| Registered as | DI **singleton** |

It is the gravity well of the codebase — Phase J added gauge tiles to it, and every subsequent feature will too unless something changes. This is the pressing case, so it goes first and the pattern it establishes governs later modularization.

## Locked rules

- **Behavior-preserving.** No feature work, no UX changes, no "while I'm in here" fixes. A behavior change hiding inside a 2,000-line move is unreviewable.
- **One extraction per PR, green at every step.** Never a big-bang rewrite.
- **The AXAML binding surface stays intact until the end.** Extract to child ViewModels, then delegate from the parent so existing bindings and tests keep working; migrate `RunTestView.axaml` to the child paths as a separate, mechanical step.
- **Children are owned objects, not DI singletons.** The parent is already a singleton; registering eight more multiplies global mutable state rather than reducing it.
- **The dispatcher pump stays in the parent.** `ScheduleUiFlush` / `DrainUiFlush` / `RunOnUiAsync` (including its headless fallback) remain coordinator concerns so children stay dispatcher-agnostic and unit-testable without Avalonia.

## Target split

| Child | Owns | Rough source |
| --- | --- | --- |
| `OperatorSessionPanelViewModel` | DUT confirm / same-DUT / change / stale | `ConfirmSession`, `ConfirmSameDut`, `ChangeSession`, `ConfirmDut`, `ChangeDut` |
| `ProgramSelectionViewModel` | Catalog, plan load, open from disk | `Programs`, `RefreshProgramsAsync`, `OpenPlanFileAsync` |
| `RunExecutionViewModel` | Run / Run Selected / Cancel, hero, progress, verdict | `ExecuteRunAsync`, `Cancel` |
| `StepTreeViewModel` | Hierarchy, rows, stages, subsections, filters, search, fail navigation | `Hierarchy`, `StepRows`, `Stages`, `Subsections`, `NestedSubsections`, `StepListItems`, `NextFail`, `PrevFail`, `JumpToCurrent`, `FilterFail`, `SetStepFilter`, `ToggleCompact` |
| `StepDetailViewModel` | Detail lines, key/values, attempt history, detail region toggles | `DetailLines`, `DetailKeyValues`, `AttemptHistoryLines`, `OpenSelectedDetail` |
| `InteractionHostViewModel` | Mid-run operator interaction fields and Continue | `InteractionFields`, `ContinueOperator` |
| `StationOverridesViewModel` | Parameter fields, Apply & save, debug patch | `ParameterFields`, `ApplyParametersAsync`, `ApplyDebugPatch` |
| `LivePresentationViewModel` | Live plot feed and gauge tiles | `PresentationTiles`, `_gaugeTiles`, plot filtering |
| `RunTestViewModel` (remaining) | Composition, cross-VM events, navigation requests, the UI flush pump | target ~300 lines |

## Sequencing

Ordered by risk, lowest first, so the pattern is proven on cheap extractions before the dangerous ones:

1. `StepDetailViewModel` — most self-contained; establishes the delegation pattern.
2. `InteractionHostViewModel` — small, well covered by existing tests.
3. `OperatorSessionPanelViewModel` — clear boundary, already backed by `OperatorSession` in Core.
4. `ProgramSelectionViewModel`.
5. `StationOverridesViewModel`.
6. `LivePresentationViewModel` — **threading-sensitive**; do not attempt before the pump ownership decision above is settled in code.
7. `StepTreeViewModel` — largest and most heavily tested; benefits most from the pattern being boring by now.
8. `RunExecutionViewModel` — last, because it touches everything else.
9. Migrate `RunTestView.axaml` bindings to child paths and delete the delegating passthroughs.

## Risks to name up front

- **Shared mutable state between clusters.** Selection, current step, and run status are read by nearly every cluster. Decide early whether they live on the coordinator (recommended) or in a small shared observable state object, and apply that decision uniformly.
- **Cross-cluster reactive subscriptions.** Moving a `[Reactive]` property across a class boundary changes when `WhenAnyValue` chains fire. Watch for ordering-dependent tests; treat any that break as a real finding, not a test to adjust.
- **Test churn.** Existing tests bind to `RunTestViewModel` members. The delegation step keeps them compiling; only step 9 forces renames, and by then the extractions are proven.

## Tests

- Existing 60 `RunTestViewModelTests` pass **unchanged** through steps 1–8. Any that need editing indicate a behavior change — stop and investigate.
- Each child gets its own test file; new tests construct the child directly, without the parent.
- E2E suite unchanged throughout — it exercises the shell, which is exactly the invariant here.
- Add a line-count guard to the [Phase 2](phase-2-architecture-tests.md) suite: no file in `Features/` over ~600 lines. Crude, but it is what stops the regrowth this phase exists to undo.

## Exit criteria

- [ ] No file in `Features/RunTest/` exceeds ~600 lines.
- [ ] `RunTestViewModel` is a coordinator: composition, events, dispatcher pump.
- [ ] Each child is constructible and unit-testable without the parent.
- [ ] No behavior change observable from the E2E suite.
- [ ] The pattern is written down for use on `ResultsViewModel` and `InstrumentsViewModel` next.

## Out of scope

- `ResultsViewModel`, `InstrumentsViewModel`, `SettingsViewModel` — later, using this pattern.
- Changing the DI lifetime of `RunTestViewModel` itself.
- Introducing a new MVVM framework, navigation library, or messaging bus.
- Reworking `HierarchyRollup` or the step row model.
