# Phase H — Result pipeline export

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** run folder layout ([FileRunStore](../../src/HardwareTest.Core/Runs/FileRunStore.cs))  
**Unblocks:** MES/QA handoff without scraping UI

## Goal

Optional file ResultListener (CSV and/or SQLite / published-table dump) beside the run folder, in addition to Typst. Typst remains the operator PDF path.

## Locked rules

- Do not replace Typst.
- Toggle via `AppSettings` (default off or on for sample — prefer default off to keep CI light).

## Work items

1. **Listener:** implement export `ResultListener` that writes under `runs/{runId}/` (e.g. `results.csv` or `opentap-results/`).

2. **Wire:** `OpenTapSession.RunAsync` attaches listener when setting enabled.

3. **Settings + UI:** checkbox on Settings; document MES handoff in adapting/opentap-platform.

4. **Tests:** host run with export on produces non-empty artifact; off leaves no file.

## Exit criteria

- Adapters can enable export and collect OpenTAP-shaped tables from disk.
- Typst PDF path unchanged.

## Out of scope

- Full OpenTAP HTML report UI inside Avalonia.
- Remote result upload.
