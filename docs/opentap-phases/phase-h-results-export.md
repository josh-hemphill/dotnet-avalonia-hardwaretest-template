# Phase H — Result pipeline export

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** run folder layout ([FileRunStore](../../src/HardwareTest.Core/Runs/FileRunStore.cs))  
**Unblocks:** MES/QA handoff without scraping UI  
**Status:** Done

## Goal

Optional file ResultListener (CSV) beside the run folder, in addition to Typst. Typst remains the operator PDF path.

## Locked rules

- Do not replace Typst.
- Toggle via `AppSettings.ExportOpenTapResults` (default **off** to keep CI light).

## Implementation

1. **Listener:** [`OpenTapFileResultExportListener`](../../src/HardwareTest.OpenTap.Host/OpenTapFileResultExportListener.cs) writes one CSV per OpenTAP `ResultTable.Name` under `{DataDirectory}/runs/{runId}/opentap-results/{Table}.csv` (header: `StepRunId` + column names).
2. **Wire:** [`OpenTapSession.RunAsyncCore`](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs) attaches the listener when export is enabled and `DataDirectory` is set.
3. **Settings + UI:** Settings checkbox “Export OpenTAP results (CSV)”; documented in [adapting.md](../adapting.md) §7.
4. **Tests:** host run with export on → non-empty CSV; off → no `opentap-results` folder.

## Exit criteria

- Adapters can enable export and collect OpenTAP-shaped tables from disk.
- Typst PDF path unchanged.

## Out of scope

- Full OpenTAP HTML report UI inside Avalonia.
- Remote result upload.
- SQLite dump (CSV only for this phase).
