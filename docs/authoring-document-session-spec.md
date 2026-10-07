# Package 05: Document sessions and edit operations

Base: merged protection milestone `cfce086` on `latest`. One PR delivers this package; packages 06–10 remain separate dependent work. Goal: editing coordination lives in Avalonia-free services, selected steps retain stable identities, independent program histories can restore every nested value, and saved-content comparisons drive dirty state.

## Contracts and implementation

- `ProgramDraft` nodes (setup and measure) gain stable identifiers independent of names, channels and indexes. Record `with` updates preserve IDs; newly inserted nodes get new IDs. Import maps existing OpenTAP step IDs and compilation preserves supported-node mappings. Raw XML remains intact. Cleanup is a policy row with stable session identity, rather than a new execution step contract.
- `AuthoringDocumentSnapshot` (or equivalent focused clone utility) defensively copies sidecar JSON, setup, recursive measure nodes, instrument bindings, settings dictionaries, expression inputs, transfer arrays, limits/history and cleanup membership. Unsupported model variants fail explicitly; snapshots never expose stored mutable history state.
- `AuthoringDocumentSession` owns program snapshots, monotonically increasing revisions, saved plan/sidecar baselines, per-program selected node and history. `AuthoringHistory` stores isolated before/after snapshots. Content identity is canonical and ignores presentation selection/revision; returning to saved content clears dirtiness. A successful sidecar-only save updates only its baseline; failed saves update neither. A new program remains unsaved even after Undo. Successful full Save retains Undo but advances both saved baselines.
- `AuthoringEditService` applies explicit program operations with read-only guards, groups a logical operation into one history entry, and rejects stale/deleted targets. Each committed field edit is one entry; no time-based typing coalescing yet. Failed/no-op edits neither add history nor clear Redo. A new edit after Undo clears Redo for the affected history.
- Workspace definitions participate in the same snapshot/save model. A catalog operation is one atomic history transaction across the manifest and affected drafts, with safe Undo/Redo. Never restore a stale catalog transaction over intervening edits: either coordinate histories or disable/reject it with an actionable reason. File-backed whole-program deletion remains explicitly irreversible; clear related history so Undo cannot falsely resurrect deleted files. Program creation remains the existing flow; no new wizard.
- `AuthoringDependencyIndex` projects stable-node references to produced/input channels, logical instruments and cleanup membership recursively, retaining raw/opaque uncertainty. It reports duplicate/missing references, without changing legacy algorithm execution semantics.
- `AuthoringIssueService` produces editing findings with stable plan/node targets (e.g. duplicate channels, missing referenced channels/instruments and duplicate IDs) separately from compiler/contract findings. Do not silently rewrite bindings or block saving merely due to an editing warning.
- The workspace view model delegates snapshot/history/selection/dirty coordination to these services. Existing setters, sequence edits, catalog edits, Save/Save All, open/close and packaging guards continue to work. Undo/Redo is exposed through visible selected-program controls and a clearly scoped workspace operation if needed, with read-only/availability guards. No new keyboard shortcuts that intercept text editing in this package.

Pseudo-code:

```text
Open -> isolate imported drafts -> create document sessions and saved baselines
Select -> persist old program's stable node -> restore new program's stable node
Edit -> capture isolated before -> apply -> validate target/IDs -> capture after
     -> if content changed: commit one transaction, advance revision, recompute dirty and issues
Undo/Redo -> verify transaction applies to current content -> restore isolated snapshot
          -> restore stable selection, advance revision, recompute dirty and issues
Save -> persist -> advance only the baselines actually written, preserving history
Catalog edit -> group manifest and affected drafts -> commit once -> Save All persists them
```

## Verification

- Rename/insert/delete and repeat unwrapping preserve unrelated setup/measure IDs and selected-node identity; supported compiled steps retain IDs after save/reload.
- Mutable sidecars, nested dictionaries, expression inputs/sources, transfer coefficients/methods, limits/history, setup and cleanup do not alias snapshots; Undo/Redo restores exact content and independent per-program history/selection.
- Edit/Undo/save/Redo/save failure and sidecar-only save prove dirtiness follows the last successfully written content. New programs remain dirty until first full save.
- Catalog edits group affected programs and definitions atomically, cancellation/no-op and stale/foreign history cannot change unrelated content, read-only sessions cannot mutate, and file-backed deletion cannot be undone.
- Dependency/issue targets remain stable across rename/reorder and include nested nodes plus opaque uncertainty.
- Expand stale destructive-impact tests with one isolated mutation per expression, transfer, limit/history, metric/setup category, not combinations that could mask omissions.
- Run full authoring Core, headless UI, architecture and CI-task suites; formatting and whitespace checks for changed files; independent reduced-context review after publishing.

## Boundaries and risks

Out of scope: durable JSON drafts/migration/crash recovery (06), criterion/runtime changes (07), typed adapters (08), immutable build artifacts (09), responsive subprocess work (10), shell redesign and expert keyboard palette (11/22). Snapshot comparisons must not confuse successful Save with edits; UI refreshes must not create history. Stable IDs and mutable OpenTAP sidecars are shared boundaries, so dependent implementation waits for this package's review. Keep focused services below the repository's feature-size guidance.
