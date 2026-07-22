# Phase D — Mixin support

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase C](phase-c-parameters.md)  
**Unblocks:** complex product-specific step behavior without forking every step type

## Goal

First-class mixin awareness: load mixin assemblies, show mixin-backed settings in Inspect/parameters, edit via the parameter bridge, ship one demo mixin.

## Locked rules

- Mixins are authored/attached in OpenTAP Editor; shell runs and edits values.
- Avalonia does not implement “Add Mixin” Editor UX in this phase (document Editor workflow).
- Custom mixins load through existing plugin directory / package install paths.

## Work items

1. **Load path:** ensure mixin assemblies in `OpenTapPluginDirectories` / Packages are searched (same `EnsurePlugins`).

2. **Inspect / parameters:**
   - Group UI for mixin-embedded properties (TypeData / `EmbedProperties`).
   - Get/set through Phase C bridge; document limitations if a member is non-writable.

3. **Sample mixin** in Plugins.Basic or `HardwareTest.OpenTap.Plugins.Mixins`:
   - Small useful example (annotation string, or break-loop-style flag if BasicSteps loops exist later).
   - Builder with `[MixinBuilder(...)]`; note in a fixture TapPlan or docs how to attach in Editor.

4. **Docs:** authoring `IMixinBuilder` / `IMixin` for product cases; CI must load the mixin assembly.

5. **Tests:** host loads assembly; Fake/VM can show a synthetic mixin group; optional host test that TypeData sees embedded member after attach (if attachable in-code without Editor).

## Exit criteria

- Demo mixin assembly loads in CI.
- Mixin properties visible in Inspect/parameters when present on a step.
- Adapting/opentap-platform docs describe custom mixin workflow.

## Out of scope

- Full mixin marketplace / package feed install (Phase E is list-only).
- Parallel/multi-DUT.
