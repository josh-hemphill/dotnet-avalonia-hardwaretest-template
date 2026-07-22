# Phase G — Sweep / loop progress chrome

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** stable Run board + result listener ([OpenTapSession](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs))  
**Unblocks:** long sweep plans that no longer look “stuck”

## Goal

Surface iteration progress for common OpenTAP flow steps (Sweep, Repeat, etc.) on the Run board — chip/hero text only, not a flowchart editor.

## Locked rules

- No flow-chart / graph editor.
- Detection via type names / OpenTAP APIs; tolerate unknown flow steps as opaque groups.

## Work items

1. **Detection:** identify Sweep/Repeat (and similar BasicSteps) in the step tree or during execute.

2. **Progress:** publish iteration index/total via `ProgressResultListener` or step events into `OpenTapProgress` / stage chip.

3. **UI:** hero or stage progress text shows `i/N` when available.

4. **Fixture:** small TapPlan/factory with a short sweep for host + VM tests.

5. **Docs:** what is and is not shown for nested sweeps.

## Exit criteria

- Fixture sweep updates iteration chrome during run in host or VM test.
- Unknown plans still run without errors.

## Out of scope

- Editing sweep bounds in UI (use Phase C parameters if exposed).
- Parallel step visualization.
