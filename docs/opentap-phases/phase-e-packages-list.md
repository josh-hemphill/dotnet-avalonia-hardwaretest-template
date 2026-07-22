# Phase E — Packages list (offline install)

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** architecture doc (can parallelize with B–D)  
**Unblocks:** operators verifying bench plugins without SSH

## Goal

In-app **list** of installed OpenTAP packages and configured plugin search directories. Install/update remains offline (CLI / image bake).

## Locked rules

- No package feed browser, no in-app install/update from network.
- Actions limited to refresh + reveal/copy paths.

## Work items

1. **Discovery service** (Host or Core-adjacent):
   - Enumerate installed packages via OpenTAP package APIs and/or known install directories.
   - Enumerate `AppSettings.OpenTapPluginDirectories` + env `HARDWARETEST_OPENTAP_PLUGIN_DIRS` + Basic plugin dir.

2. **UI:** Settings subsection or lightweight Packages page:
   - Package name, version, path (when known).
   - Plugin directories list.
   - Refresh command; copy path / open folder where OS allows (no-op or status message on locked appliances).

3. **Appliance docs:** bake `.TapPackage` or run `tap package install` during provisioning; then list populates ([appliance-linux.md](../appliance-linux.md)).

4. **Tests:** directory listing smoke with temp dirs; Fake catalog if package API is hard to host in CI.

## Exit criteria

- Engineer can open the UI and see plugin dirs + any discoverable packages.
- Docs describe offline install path clearly.

## Out of scope

- Feed configuration, download, uninstall from UI.
- Signing / trust UI.
