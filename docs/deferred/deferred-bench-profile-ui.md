# Deferred — Bench profile / ComponentSettings UI

**Parent:** [opentap-platform.md](../opentap-platform.md) · [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** Instruments page + `PlanSlotOverrides` (Phase F — Done)

## Goal

Provide an Avalonia-owned bench profile editor that can view/edit a useful subset of OpenTAP ComponentSettings / instrument defaults without cloning OpenTAP Editor.

## Locked decisions

- Avalonia owns UI; no native OpenTAP settings windows on appliance.
- Golden `.TapPlan` files remain Editor-authored; bench profile is **station overlay**, persisted in settings or a dedicated profile document under `DataDirectory`.
- Start with instrument resource defaults + a curated property allow-list — not a full TypeData property grid for every plugin.
- Engineer mode required to edit; operators consume effective bindings via Instruments / Run.

## Workstreams

1. Profile document schema (version stamped — Phase 5 gates).
2. Host APIs to enumerate editable ComponentSettings members safely.
3. UI: profile select, bind roles → resources, optional numeric defaults.
4. Apply profile before Run (alongside existing PlanSlotOverrides).
5. Tests: schema round-trip; apply changes reflected in InstrumentSlots.

## Exit criteria

- [ ] Engineer can save/load a bench profile and see it applied on Run
- [ ] Operators cannot edit profiles without Engineer mode
- [ ] Invalid profile fails loudly (schema gate), does not silently coerce

## Out of scope

- Full OpenTAP Editor parity
- Mixin attach/detach in Avalonia (attach remains Editor — Phase D)
- Multi-bench cloud profile sync

## Dependencies

- Phase F resources (Done)
- Schema stamping (Phase 5 — Done); full migrator still deferred
