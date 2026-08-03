# Deferred — Auto-update / delivery channel

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** [deferred-appliance-kiosk.md](deferred-appliance-kiosk.md)

## Goal

Define how sealed or desktop benches receive application updates without manual USB copies on every station.

## Locked decisions

- Appliance updates prefer **image/A-B partition or layered app replace** over in-place file patching.
- Desktop (Windows) may use a signed installer channel; still no silent elevation mid-run.
- Never update while a plan is running; Safety Stop / idle required.
- Update checks are opt-in or engineer-scheduled on air-gapped sites (offline package drop).

## Workstreams

1. Version manifest format (semver + min schema + OpenTAP plugin compatibility).
2. Desktop: signed artifact fetch + apply on restart.
3. Appliance: document image flash / ostree / dual-partition flow (pick one stack when scheduled).
4. In-panel “update available” banner (Engineer); operators see “ask engineer” if desired.

## Exit criteria

- [ ] Documented channel delivers a newer build and restarts into it
- [ ] Active run blocks apply
- [ ] Rollback path documented for appliance

## Out of scope

- Updating OpenTAP plugin feeds automatically ([deferred-package-feed-install.md](deferred-package-feed-install.md))
- Delta binary patching research project

## Dependencies

- Build info surface (Phase 4 — Done)
- Appliance layout ([appliance-linux.md](../appliance-linux.md))
