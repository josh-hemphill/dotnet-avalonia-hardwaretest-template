# Phase D — Mixin support

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase C](phase-c-parameters.md)  
**Status:** Done  
**Unblocks:** complex product-specific step behavior without forking every step type

## Goal

First-class mixin awareness: load mixin assemblies, show mixin-backed settings in Station overrides, edit via the parameter bridge, ship one demo mixin.

## Locked rules

- Mixins are authored/attached in OpenTAP Editor or OpenTAP TUI; shell runs and edits values.
- Avalonia does not implement “Add Mixin” Editor UX in this phase (document Editor workflow).
- Custom mixins load through existing plugin directory / package install paths.

## Delivered

1. **Load path:** [`OpenTapPluginSearch`](../../src/HardwareTest.OpenTap.Host/OpenTapPluginSearch.cs) registers Basic + Visa + Mixins DLLs; `OpenTapPluginDirectories` / `HARDWARETEST_OPENTAP_PLUGIN_DIRS` remain for adopters.
2. **Parameters:** [`OpenTapParameterBridge`](../../src/HardwareTest.OpenTap.Host/OpenTapParameterBridge.cs) tags `IsMixinEmbedded`, groups Display/`Annotation` labels; Station overrides panel shows them (Engineer/Debug).
3. **Sample mixin:** [`HardwareTest.OpenTap.Plugins.Mixins`](../../src/HardwareTest.OpenTap.Plugins.Mixins/) — `AnnotationMixin` + `AnnotationMixinBuilder`. Sample Identity Check attaches via [`OpenTapMixinAttach`](../../src/HardwareTest.OpenTap.Host/OpenTapMixinAttach.cs) (`DynamicMember.AddDynamicMember` + instance init; `MixinFactory` is internal in OpenTAP 9.32).
4. **Docs:** adapting “Custom mixins” + platform mixin model.
5. **Tests:** host discovers builder; enumerate/set/get Note; Fake VM shows Annotation group on Identity.

## Authoring checklist (product mixins)

1. New plugin class library referencing OpenTAP (same version as Basic).
2. Implement embed type (`IMixin`) with `[Display]` settings.
3. Implement `[MixinBuilder(typeof(ITestStep))]` `IMixinBuilder` → `MixinMemberData` + `EmbedPropertiesAttribute`.
4. Ship DLL next to Basic or add folder to `OpenTapPluginDirectories`.
5. In **OpenTAP Editor** or **OpenTAP TUI**: add mixin → Annotation (or your builder) → save `.TapPlan`. Validate with `HardwareTest --validate-plan` / `HardwareTest.PlanValidate`.
6. On the bench: Engineer/Debug → select step → Station overrides → edit mixin fields → Apply & save.

## Out of scope

- Full mixin marketplace / package feed install (Phase E is list-only).
- Parallel/multi-DUT.
- Avalonia “Add Mixin” UI.
