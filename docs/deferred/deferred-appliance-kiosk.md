# Deferred — Appliance kiosk / image bake

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** [appliance-linux.md](../appliance-linux.md), [containers.md](../containers.md), `Containerfile.appliance`, `deploy/quadlets/`

## Goal

Turn the portable `linux-x64` publish into a **sealed appliance image**: kiosk session, systemd/quadlet units, read-only app + writable data partition, offline plugin bake.

## Locked decisions

- **Podman + quadlets** over docker compose ([containers.md](../containers.md)).
- Layout remains `app/` (immutable) + `data/` (writable) per [appliance-linux.md](../appliance-linux.md).
- No on-disk operator session resume file — DUT confirm stays in-process.
- `AllowOsFolderBrowse=false` on appliance profiles; Engineer overlays via env if needed.
- Still **no** OpenTAP floating dialogs on the sealed UI.

## Workstreams

1. Bake script: publish self-contained `linux-x64`, copy plans/plugins/Typst into image layers.
2. systemd user/system units or quadlets for autostart + restart on crash.
3. Kiosk compositor / single-app session (document chosen stack: Cage, Weston, etc.).
4. Data volume mount + permissions; log + crash + runs under `data/`.
5. Smoke: boot VM/image, confirm DUT, run sample (mock VISA), export to USB path.

## Exit criteria

- [ ] Documented image build produces a bootable appliance artifact
- [ ] App starts kiosk-style without desktop chrome the operator can leave casually
- [ ] Data survives image upgrade; app layer is replaceable
- [ ] Quadlet/unit files checked in and referenced from containers.md

## Out of scope

- Windows kiosk packaging
- Secure boot / TPM attestation
- Auto-update channel ([deferred-auto-update.md](deferred-auto-update.md))

## Dependencies

- Green `linux-x64` CI advisory path (Phase 7 — Done)
- Hardware readiness gates for mock vs real VISA on the target bench
