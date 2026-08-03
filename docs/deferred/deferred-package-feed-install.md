# Deferred — In-app OpenTAP package feed install

**Parent:** [opentap-platform.md](../opentap-platform.md) · [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** [phase-e-packages-list.md](../opentap-phases/phase-e-packages-list.md) (Done — list only)

## Goal

Allow Engineer mode to install/update OpenTAP packages from a configured feed **inside the shell**, without requiring a separate terminal on every bench — while keeping sealed appliances provisioning-first.

## Locked decisions

- Default product posture remains **offline bake / `tap package install`**. In-app install is an Engineer escape hatch, not the operator path.
- No floating OpenTAP Package Manager window — Avalonia-owned UI only.
- Feeds are explicitly configured (URL + credentials via env/settings); no browsing the public internet by default on appliances.
- Install requires process quiet (no active run); may require app restart after plugin load.

## Workstreams

1. Settings → Packages: Add feed, list available versions (Host API wrapping `tap` or OpenTAP package APIs).
2. Install / uninstall with progress in-panel; refresh package list.
3. Failure copy for missing runtime, hash mismatch, disk full.
4. Tests: Fake catalog; optional integration behind an opt-in trait (network).

## Exit criteria

- [ ] Engineer can install a package from a configured feed and see it in the list after refresh
- [ ] Operator (non-Engineer) cannot install
- [ ] Active run blocks install with clear Status
- [ ] Appliance docs describe when to prefer bake vs in-app install

## Out of scope

- Dependency resolver UI beyond what OpenTAP already guarantees
- Publishing packages from the shell
- Auto-update of packages ([deferred-auto-update.md](deferred-auto-update.md))

## Dependencies

- Phase E list UI (Done)
- Engineer mode gating (landed)
