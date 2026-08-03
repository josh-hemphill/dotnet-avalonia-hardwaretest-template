# Phase E — Packages list (offline install)

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** architecture doc (can parallelize with B–D)  
**Unblocks:** operators verifying bench plugins without SSH  
**Status:** Done

## Goal

In-app **list** of installed OpenTAP packages and configured plugin search directories. Install/update remains offline (CLI / image bake).

## Locked rules

- No package feed browser, no in-app install/update from network.
- Actions limited to refresh + reveal/copy paths.

## Implementation

- Host: [`OpenTapPackageCatalog`](../../src/HardwareTest.OpenTap.Host/OpenTapPackageCatalog.cs) + `IOpenTapSession.ListPluginDirectories` / `ListInstalledPackages` (filesystem `package.xml` + Basic/Mixins/settings/env/`PluginManager` dirs).
- UI: Settings subsection **OpenTAP packages & plugins** ([`SettingsViewModel`](../../src/HardwareTest/Features/Settings/SettingsViewModel.cs)) — Refresh, Copy path, Open folder.
- Fake catalog for ViewModel tests; Host temp-dir smoke in `OpenTapHostTests`.

## Exit criteria

- Engineer can open Settings and see plugin dirs + any discoverable packages.
- Docs describe offline install path clearly ([appliance-linux.md](../appliance-linux.md)).

## Out of scope

- Feed configuration, download, uninstall from UI.
- Signing / trust UI.
