# Phase 5 — Schema versioning

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 4](phase-4-build-info.md) (records the writing app version alongside the schema version)
**Unblocks:** shipping to users you cannot ask to delete their data directory
**Status:** Not started

## Goal

Stamp a schema version on every persisted document, and make the read path **explicit** about legacy and future data instead of silently coercing it.

Building a general migration engine is deliberately deferred. This phase only guarantees you can tell what you are reading and refuse to corrupt it.

## Why now, concretely

The Phase I/J work just added five fields to `StoredSample` (`LimitLow/High`, `HistoryEnabled`, `HistoryWatchPercent`, `HistoryAlertPercent`) and a `Reports` collection to `TestRunRecord`. Nothing marks old records as old.

[`DutHistoryService`](../../src/HardwareTest.Core/Runs/DutHistoryService.cs) reads back the last ten runs for a DUT and compares metrics. A pre-Phase-I record deserializes with C# defaults, so absent thresholds become *default* thresholds and an absent `HistoryEnabled` becomes `false` — the comparison still runs and still produces a Watch/Alert verdict. That output looks authoritative and may be wrong. **Unknown must be distinguishable from default**, and this is the phase that makes it so.

This gets worse, not better, with every field added. The window to do it cheaply is now, before real users have run history worth keeping.

## Locked rules

- **Never silently downgrade.** A document written by a newer app is read-only to an older one. Losing an operator's run history to a version rollback is unacceptable; refusing to overwrite it is merely inconvenient.
- **Legacy is a first-class state, not a default value.** Reads must be able to report "this field was absent."
- Version is an `int`, monotonically increasing, one per document type. No semver, no dates.
- Bumping a version is a **deliberate, reviewed act** with a changelog row — not a side effect of adding a property.

## Work items

1. **Add `SchemaVersion`** to `AppSettings`, `UiState`, `TestRunRecord`, and `SuiteRunRecord`. Current shipped shape is version **1**. Register in [`AppJsonContext`](../../src/HardwareTest.Core/Serialization/AppJsonContext.cs).

2. **Central constants** — one `SchemaVersions` static holding the current version per document type, so the value is greppable and cannot drift between reader and writer.

3. **Read-path gate** shared by [`SettingsStore`](../../src/HardwareTest.Core/Settings/SettingsStore.cs) and [`FileRunStore`](../../src/HardwareTest.Core/Runs/FileRunStore.cs):

   | Stored version | Behavior |
   | --- | --- |
   | absent or `0` | Treat as **legacy**. Load, flag `IsLegacy`, log once per file. |
   | `== current` | Normal. |
   | `< current` | Run registered upgrade steps. None exist yet, so this is identity plus a log line — the hook is the deliverable. |
   | `> current` | **Load read-only.** Do not write back. Surface a clear in-panel warning naming both versions and the app version that wrote it. |

4. **Write path** always stamps the current version, and refuses to write over a document flagged read-only by rule 3.

5. **Make "unknown" representable where it matters.** Change the Phase I history fields on `StoredSample` to nullable, and update `DutHistoryService.ResolveHistoryPolicies` to skip a metric whose policy is unknown rather than assuming defaults. A legacy record should produce "no comparison available", not a confident delta.

   While in that method: it currently takes the **last** sample per metric key to resolve policy, which is order-dependent if samples are not time-ordered. Make the selection explicit.

6. **Legacy visibility** — Results shows a quiet badge on runs loaded as legacy or read-only, so an operator comparing two runs can see that one predates a contract change.

7. **Docs** — a schema table in [adapting.md](../adapting.md): document type, current version, what changed at each bump. This table is the migration spec when the deferred engine finally gets built.

## Tests

- A version-1 document with no `SchemaVersion` loads and is flagged legacy.
- A document with `SchemaVersion` above current loads read-only and a save attempt is rejected.
- Round-trip stamps the current version.
- `DutHistoryService` given a legacy record reports no comparison rather than a default-threshold verdict.
- Fixture files for each supported version live under `tests/fixtures/schema/` and are checked in — a schema gate with no golden files is not a gate.

## Exit criteria

- [ ] Every persisted document carries a schema version.
- [ ] Downgrading the app cannot overwrite newer run history.
- [ ] A pre-Phase-I run record cannot produce a false history alert.
- [ ] The upgrade hook exists and is exercised by at least one no-op registration.

## Out of scope

- A general migration engine, data-conversion CLI, or bulk re-stamping tool — deferred by [locked decision](../platform-roadmap.md#locked-product-decisions).
- Backfilling `AppVersion` into historical records.
- Versioning the OpenTAP `.TapPlan` files (OpenTAP owns that format).
