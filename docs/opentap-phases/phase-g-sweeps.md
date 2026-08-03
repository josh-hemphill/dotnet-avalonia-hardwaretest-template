# Phase G — Sweep / loop progress chrome

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** stable Run board + result listener ([OpenTapSession](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs))  
**Unblocks:** long sweep plans that no longer look “stuck”  
**Status:** Done

## Goal

Surface iteration progress for common OpenTAP flow steps (Sweep, Repeat, etc.) on the Run board — chip/hero text only, not a flowchart editor.

## Locked rules

- No flow-chart / graph editor.
- Detection via type names / OpenTAP APIs; tolerate unknown flow steps as opaque groups.
- Nested loops report the **innermost** active loop only.

## Implementation

1. **Detection:** [`OpenTapLoopProgress`](../../src/HardwareTest.OpenTap.Host/OpenTapLoopProgress.cs) — `RepeatStep`, `RepeatLoopStep`, `SweepLoop`, `SweepLoopRange`, `SweepParameterStep`, `SweepParameterRangeStep`; totals via `Count` / `SweepPoints` / enabled rows.
2. **Progress:** `ProgressResultListener` loop stack → `OpenTapProgress.IterationIndex` / `IterationTotal` / `IterationText`.
3. **UI:** Run hero status includes `iter i/N` ([`RunTestViewModel.RefreshHero`](../../src/HardwareTest/Features/RunTest/RunTestViewModel.cs)).
4. **Fixture / catalog:** [`RepeatLoopStep`](../../src/HardwareTest.OpenTap.Plugins.Basic/Steps.cs) + [`SweepDemoProgramFactory`](../../src/HardwareTest.OpenTap.Host/SweepDemoProgramFactory.cs) (`sweep-repeat.TapPlan`); built-in **sweep-demo** in [`ProgramCatalog`](../../src/HardwareTest.OpenTap.Host/ProgramCatalog.cs).

## Exit criteria

- Fixture sweep updates iteration chrome during run in host or VM test.
- Unknown plans still run without errors.

## Out of scope

- Editing sweep bounds in UI (use Phase C parameters if exposed).
- Parallel step visualization.
