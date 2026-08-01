# Phase 10 — Export, storage hygiene, Run Selected cleanup, chrome polish

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 3](phase-3-configuration-model.md), [Phase 6](phase-6-crash-reporting.md), [Phase 9](phase-9-runboard-decomposition.md)
**Status:** Done

## Goal

Operator-facing export without Explorer, free-space/retention so small disks stay usable, a clear Run Selected cleanup policy, and a batch of Run/transport chrome polish.

## Locked decisions

- **Provisioning vs operator:** plans, Typst overrides, plugins, and settings stay image/env concerns.
- **Export:** in-app **Export to…** for certification/status/run packages and crash support zips → removable media and/or `HARDWARETEST_EXPORT_DIRECTORY`.
- **Run Selected cleanup:** default **always include `SafeShutdownStep`**. Disabled siblings showing NotExecuted/Invalidated is expected. Opt out via `selectionIncludesCleanup: false` in `{planId}.program.json`.
- **Retention:** age + count caps on run folders; warn/block before runs when free space is low.
- **UI polish:** layout/styling only — no behavior changes beyond transport Continue styling.

## Workstreams

### A — Storage: free space, retention, export

Config keys (Phase 3 binder): `ExportDirectory`, `PreferRemovableExport`, `RunRetentionDays`, `RunRetentionMaxRuns`, `DataFreeSpaceWarnBytes`, `DataFreeSpaceCriticalBytes`, `AllowOsFolderBrowse`.

Services: `IStorageHealthService`, `IRunRetentionService`, `IExportTargetService`.

UI: Results export actions; Home crash export via same targets; Engineer-gate Open folder.

### B — Run Selected cleanup

Verify SafeShutdown executes on selection runs. Catalog flag `selectionIncludesCleanup` (default true). Document Invalidated siblings.

### C — Chrome polish

Confirmed DUT strip density; toolbar bottom margin; single Details toggle (remove Hide); green Continue on transport bar; icon-centered footer when nav pane is compact.

## Exit criteria

- [x] Free-space warn/block before Run; retention prunes old runs without touching in-progress
- [x] Operator can export certification/status/run package and crash zip to removable media or `ExportDirectory` without Explorer
- [x] Open folder / Open plan from disk are Engineer-gated or hidden when `AllowOsFolderBrowse` is false
- [x] Run Selected runs SafeShutdown by default; opt-out documented; Invalidated siblings explained
- [x] Listed chrome polish items done; E2E smoke still green

## Out of scope

- In-app file browser
- Remote MES upload / third-party sync
- Clock/NTP discipline
- Splitting `IOpenTapSession`
