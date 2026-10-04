# Remaining authoring stack: packages 06–22

Goal: an engineer can create a workspace and incomplete test plan, configure actual hardware and criteria, explore formulas against recordings, save/recover drafts, compile/check/package an identifiable saved revision, and use the whole workflow through accessible forms and expert commands. Manual review will use the top of the full stack. Every package remains a separate PR with its own contract, tests and independent review; none is merged by this workflow.

The detailed product scope and acceptance criteria in [the implementation plan](authoring-implementation-plan.md) remain authoritative. This document fixes implementation contracts, data flow and sequencing before coding the remaining work. Each subsection below supplies its package's reduced-context implementation/review brief, together with the corresponding scope/acceptance subsection of the plan.

## Stack and conflict map

`latest <- 05 (#192) <- 06 <- 07 <- 08 <- 09 <- 10 <- 11 <- 12 <- 13 <- 14 <- 15 <- 16 <- 17 <- 18 <- 19 <- 20 <- 21 <- 22`

Each branch targets its preceding branch while that PR is open. If a parent merges, fetch/rebase and retarget children as needed. Additive fixes precede rebasing descendants. Do not merge on behalf of the user. No dependent package uses an unfinished contract. Default to completing review/fix for a package before implementing its successor.

| Shared surface | Packages | Coordination |
| --- | --- | --- |
| Program model, DTOs, compiler | 06–09, 12–13, 15–16, 19 | Sequential changes; roundtrip tests at each boundary |
| Runtime samples and evaluators | 07, 13, 17 | Runtime semantics first; UI consumes same evaluator |
| Build, CLI, subprocesses | 09–10, 16, 18 | Freeze requests/receipts before orchestration and views |
| Main window and view models | 11–22 | Extract shell once, then focused views; no concurrent rewrites |
| Manifest/preferences/templates | 06, 11, 19–22 | Preserve future files; additive source-generated schemas |

Common verification: meaningful focused tests for each acceptance path, appropriate authoring Core/headless UI/architecture tests, scoped formatting and whitespace, Linux build; runtime plugin tests when execution changes. After push, reduced-context independent review; fix Must/Should and repeat. Hosted CI and review comments join the same findings queue. Final verification runs full suites, publishes the complete application, and supplies reproducible manual-use steps, representative example/recording data, expected blockers and artifact locations. Native tests available only on this Linux host must not be described as Windows verification.

## 06 Versioned drafts and recovery

- Depends on: 05. Files: new Core DTO/store/recovery/migration services, loader, saving/editor partials, compiler persistence, schemas/ignore conventions and tests.
- Public surface: source-generated `AuthoringDocumentDto`/workspace DTO with schema version, program/node IDs, revision, compiled baseline hashes, sidecar and explicit setup/measure/source discriminators; `AuthoringDocumentStore.Load/Save`, recovery checkpoint service, conflict status/reconciliation actions.
- Preserve every editable value, including nested/raw nodes, settings, input arrays, cleanup, missing criteria, formula intent and incomplete numeric text keyed by stable node/field. The runtime draft retains typed valid values; incomplete text lives in durable authoring state and does not silently become zero. Exploration/deployment intent defaults compatibly for imported formulas.
- `authoring-drafts/<id>.authoring.json` and `workspace.authoring.json` are source files. `.authoring/recovery`, `.authoring/builds`, `.authoring/opentap` are local and ignored. Validate IDs, containment and symlinks before any file mutation. Unknown future schemas remain byte-preserved/read-only; corrupt known schemas report a recoverable failure.
- Pseudo-code: Open -> load manifest -> import compiled plans where no source exists -> overlay known source documents -> compare compiled baseline hashes -> expose conflict without overwriting either side. Explicit reconcile chooses imported compiled content or retained source; exporting retained source requires an explicit choice. Save -> atomically publish source -> mark source baseline; compilation is a separate explicit operation, and deployability does not gate source Save. Keep existing valid-plan Save workflows compatible where compilation succeeds; incomplete plans must still persist and reopen.
- CLI formula evaluation reads current saved authoring sources through the same source-first loader as the editor, including exploration-only and source-only formulas; future schemas and corrupt sources fail explicitly without evaluating a compiled fallback. CLI/editor validation, compatibility and packing instead require compiled artifacts matching the saved source and reject stale or uncompiled revisions.
- Recovery: edit -> debounce checkpoint of immutable snapshot -> atomic replace under local recovery -> notify success/failure on UI thread, never mark successful Save. Open offers recoverable newer checkpoints explicitly; discard/accept has defined baseline semantics. Cancel stale session timers; read-only future data never checkpoints over source. Visible failure retains draft content.
- Atomic writer: write unique temp, flush, retain last-good backup, replace; interrupted temp never supersedes verified committed document. Failed compiled pair rollback retains durable preimage and original exception if restoration fails; report backup path and recovery action. Manifest migrations are idempotent with backup and no changes to future versions.
- Tests: all variants/IDs/order/settings/raw XML/numeric text/formula intent roundtrip; incomplete Save/reopen; failed replace and rollback restoration; interrupted writes and newer checkpoints; future schema byte preservation; external compiled edits conflict; migration twice; checkpoint differs from saved dirty baseline.
- Risks/conflicts: source versus compiled baselines and old save semantics; all consumers in 07–10 and 12–13/19 depend on this contract. Out of scope: new runtime criteria, adapters, build orchestration, redesigned shell.

## 07 Criteria and runtime parity

- Depends on: 05 (sequential stack includes 06). Files: Core criteria/function/formula/compiler/preview, Basic plugin sample capture/check steps and package metadata.
- Surface: recipe criterion metadata (required values, units, inclusive bounds, numeric constraints); one authoritative `LimitSpec` mapped to runtime settings only at compile/import boundaries; channel-average runtime step and shared evaluator result.
- Pseudo-code: recipe -> criterion requirements independent of display -> validate typed criterion -> compile channel-average step referencing intended producer -> retrieve capture for this plan execution and loop iteration -> evaluate same average/verdict as preview. Clear or scope capture at producer/run/iteration boundaries; absent samples cannot fall back to stale prior data. Preserve imported legacy fresh-reading Mean GTE and its binding/execution.
- Tests: same series preview/runtime average+inclusive verdict; missing/nonfinite criteria; display role changes do not alter criterion; repeat iterations and successive runs exclude stale samples; legacy Mean GTE behavior unchanged; Save/Load preserves criterion.
- Risks/conflicts: sample lifecycle and import ambiguity; coordinate model DTO updates with 06. Out of scope: adapters and full build/UI redesign.

## 08 Typed instrument adapters

- Depends on: 05; files: adapter catalog, compiler, instrument refs/usage/function metadata, bootstrap/package inspection, DTOs.
- Surface: adapter descriptor (type/display/package, constructor/address fields, compatible functions, identity/shutdown support); explicit instrument binding on applicable algorithms, with imported unsupported resource payload preserved.
- Pseudo-code: import actual type+slot+binding -> resolve registered adapter or retain opaque content -> edit typed supported descriptor -> compile using adapter without substituting mock or first instrument. Unknown/missing package remains unavailable and yields actionable issue. Resolve legacy algorithm binding from actual OpenTAP resource, preserving non-first slots; removal/replacement follows capability and dependency checks.
- Tests: supported real and explicitly chosen mock types roundtrip, non-first legacy binding, missing package no substitution, incompatible function reject, unsupported import survives, instrument metadata/cleanup isolated stale impacts.
- Risks/conflicts: serializer/resource registration and plugin availability. Out of scope: arbitrary installed type creation and broad hardware UI.

## 09 Build saved snapshots

- Depends on: 06–08; files: shared Core build/snapshot/source-map/receipt services, pack plan/packer/CLI/compiler contracts.
- Surface: immutable `AuthoringBuildRequest`, captured source/manifest/package/environment identities, result/receipt with version, included/excluded plans, source maps, output hashes and checks.
- Pseudo-code: capture saved sources and dependency identity -> compile into unique contained staging -> strict validate -> real compatibility -> construct exact artifact list -> recheck sources/environment/symlink targets and cancellation -> publish outputs transactionally -> receipt. Changed inputs reject publication; failures preserve previous outputs. CLI and GUI share this path; no captured draft aliases.
- Tests: source/manifest/external dependency mutation after capture, symlink containment/identity, excluded plans, failed publication preserves old files, receipt matches actual artifacts; real installed external TUI process fixture, with prerequisite absence reported explicitly rather than a synthetic success.
- Risks/conflicts: older ship-manifest consumers; use optional metadata or separate receipt. Out of scope: async coordinator and Build view.

## 10 Responsive operation orchestration

- Depends on: 09; files: Core coordinator/process runner/protocol, authoring command dispatch and VM operation state.
- Surface: versioned child request/result, stage/progress/log streams, cancellation token and workspace generation, operation result; one serialized OpenTAP-sensitive lane.
- Pseudo-code: start captures workspace generation -> reject duplicate -> spawn authoring headless child with isolated environment -> consume structured results separately from logs -> marshal progress -> before publication recheck cancellation+generation -> publish or cleanup. Cancel terminates owned child tree, waits for exit, removes owned staging; late results never update a new workspace.
- Tests: deliberately slow process allows UI heartbeat, duplicate start, cancellation before publish, stale workspace completion, bounded termination/log retention, two distinct plugin environments. Architecture excludes operator worker reuse.
- Risks/conflicts: process ownership/cancellation windows. Out of scope: redesigned shell and final Build view.

## 11 Responsive main shell

- Depends on: 05; files: focused Avalonia shell/views/child VMs and layout state, MainWindow handlers.
- Surface: compact commands, Programs rail, sequence/editor, explicit Hardware/Preview/Issues/Environment/Build navigation; selection/focus/scroll restoration.
- Pseudo-code: workspace state -> focused view projections -> constrained layout chooses separate preview at narrow sizes, optional dock at wide sizes -> measured protection modal rows preserve decision controls -> restore stable node/focus when navigating. Use vertical field layout where necessary, never hide required actions outside viewport.
- Tests: nonzero visible in-window sequence/inspector/preview at960×600/1280×800, increased scaling/long labels, many programs/findings, modal decision visibility and focus restoration.
- Risks/conflicts: all later UI packages; extract once before focused features. Out of scope: complete forms/palette/onboarding.

## 12 Selected-step forms

- Depends on: 07/11; files: focused editors, child VMs and input/error projections.
- Surface: Configure/Operator display/Advanced; distinct identity/prompt/input/repeat/cleanup/raw forms; durable incomplete numeric state from06; explicit instrument binding.
- Pseudo-code: stable selected node -> recipe metadata -> appropriate form -> field edit transaction retains raw text, commits valid typed value, exposes accessible explanation; source recipe changes use explicit validation/migration. Display edits never rewrite criterion/binding.
- Tests: every node kind relevant fields, incomplete text persists, recipe changes, display/criterion independence, visible accessible errors, actual non-first instrument selection.
- Risks/conflicts: model/settings and shell selection. Out of scope: formula status/palette and future commands.

## 13 Formula deployment status

- Depends on: 06/07/12; files: lowerer status, document intent, formula editor/palette metadata/issues.
- Surface: Deployable recipe/Preview only/Invalid/Missing requirements with lower target, input/time-grid requirements, explicit exploration exclusion.
- Pseudo-code: parse -> validate input/time grid -> lower with criterion -> classify -> exploration saves unchanged and build excludes it visibly; deployment intent with unsupported formula blocks build. Offer actual supported alternatives only. Status refreshes per revision.
- Tests: std exploration Save/reopen/excluded artifact, unsupported deployment blocked, mean missing threshold target, filter irregular/missing clock parity.
- Risks/conflicts: build inclusion and criteria. Out of scope: new expression language/runtime operators.

## 14 Hardware and workspace definitions

- Depends on: 04/08/11; files: hardware/definitions views and child VMs, usage/impact services.
- Surface: logical name, actual type/address/package/usage/cleanup table; adapter-based edit/create; separate global definitions and per-program membership.
- Pseudo-code: usage projection -> edit transaction -> dependency impact -> explicit replace/remove -> atomic catalog history. Review names actual per-program changes including report fallback and kind reset. No implicit retargeting.
- Tests: type/binding roundtrip from UI, capability rejection, per-program membership isolation, consequence dialog and Undo/Redo.
- Risks/conflicts: catalog history and imported opaque usage. Out of scope: initialization wizard.

## 15 Sequence operations

- Depends on: 05/07/08/12; files: Core sequence operations/dependency checks/palette, sequence view and commands.
- Surface: searchable Measure/Check/Operator action/Flow palette with prerequisites, explicit insertion point, Rename/Duplicate/Move, special remove-loop/disable-shutdown labels; no generated Test Group insertion.
- Pseudo-code: capture selected stable target -> preflight capability/order/loop scope -> transaction; duplicate subtree allocates all new IDs/channels and remaps only internal references; retain external references. Repeat wraps selected eligible node. Opaque dependencies reject unsafe moves with explanation. Failed/no-op commands preserve history.
- Tests: selected insertion/repeat, nested duplication/internal+external references, unique IDs, dependency/loop movement rejection, raw conservatism, complete Undo.
- Risks/conflicts: model/DTO identity and source maps. Out of scope: keyboard palette shortcuts22.

## 16 Actionable findings

- Depends on: 09/11/12; files: optional structured contract targets/source maps/formatters, Issues view/navigation.
- Surface: program/node/compiled-step/field/section target, revision and staleness, severity counts, Go to field; keep text/JSON/SARIF compatible.
- Pseudo-code: producer emits structured target -> build maps compiled step to stable node -> Issues preserves checked revision -> navigation resolves workspace/program/node/section/field and focuses; unresolved target opens honest plan-wide location. Never parse free text for a target. Editing invalidates current checked state.
- Tests: missing threshold focuses correct form, plan-wide target, stale/deleted node refuses fake navigation, formatter backward compatibility.
- Risks/conflicts: compiler mappings and focus. Out of scope: new validation rules unrelated to targets.

## 17 Board preview and recordings

- Depends on: 07/11–13; files: board/data-source views, dataset import/catalog/binder and preview evaluators.
- Surface: acquisitions+derived criteria shared widgets, provenance/DUT/channels/sample/time-base details, example versus recording label; Import/Open folder/empty state.
- Pseudo-code: validate selected recording schema/channels/time -> bind evaluator from07 -> render values/verdicts with provenance; import unique contained destination atomically -> refresh/select on success; failure/collision leaves prior selection/data unchanged. Missing inputs target affected nodes; no hardware-execution claims.
- Tests: valid/invalid/colliding imports, elapsed-time retention, missing channels/time grids, average parity, actual visible board at narrow/wide sizes.
- Risks/conflicts: sample semantics/widgets. Out of scope: live acquisition from preview.

## 18 Environment and Build views

- Depends on: 09/10/16; files: environment/build child VMs/views, package import/inspection/preferences.
- Surface: required/installed/missing package versions and selected home, offline import/preparation; included/excluded saved revision/checks/output/progress/logs/receipt/history.
- Pseudo-code: environment projection -> supported prepare/import action through coordinator -> refresh; build request -> display stages -> only real completed checker marks compatibility passed -> receipt persists previous success while edit invalidates readiness. Cancellation/errors preserve previous artifacts.
- Tests: missing-package recovery, selected-home rendered and honored by successful GUI packaging, excluded plans absent, result retention across edit/fail/cancel, no success before actual checker.
- Risks/conflicts: process/session completion and plugin inspection. Out of scope: bench installation.

## 19 New test plan initialization

- Depends on: 05–08/11–14; integrate17 preview. Files: Core initializer/contracts, draft store, initialization views and Programs commands.
- Surface: `PlanInitializationRequest/Result`, `AuthoringPlanInitializer` shared by guided/direct flows; name/destination, empty/task template, hardware, setup/cleanup, optional measurement/criterion, review/create.
- Pseudo-code: normalize editable plan ID -> validate case-equivalent collisions/containment -> construct unique stable nodes/channels and chosen actual resources -> summary -> recheck destinations/external changes -> atomic source publication -> open selected draft with issues. Cancellation writes nothing. Empty/incomplete drafts allowed; never silently insert mock. Route legacy CreateProgram through the same constructor/service without changing its unsaved convenience behavior.
- Tests: duplicate/case/path collisions, unique subtree/output IDs, incomplete Save/reopen, cancellation/interrupted writes, demo/mock explicitly selected, supported template compile/check/compatibility.
- Risks/conflicts: durable store and adapter availability. Out of scope: workspace creation20 and full onboarding21.

## 20 Workspace creation and templates

- Depends on: 06/08/11/19; files: workspace initializer/template descriptors, welcome/creation UI, schema conventions.
- Surface: explicit empty/product/demo task templates with packages/file preview and workspace/package settings.
- Pseudo-code: validate destination/conflicts -> stage consistent manifest/directories/source docs -> publish only owned files -> rollback only created files on failure -> open workspace -> optionally continue19. Never overwrite unrelated files or leave valid-looking partial manifest.
- Tests: reproducible empty/task creation, conflict bytes unchanged, failure/cancel ownership cleanup, immediate save/open draft.
- Risks/conflicts: templates/schemas and paths. Out of scope: guided teaching flow21.

## 21 Guided onboarding

- Depends on: 12–14/17/19–20; files: focused onboarding coordinator/views/context help/template copy.
- Surface: name/device -> instrument -> measurement -> criterion -> preview -> save/check, direct empty route and skip optional guidance, generated identity/shutdown summary.
- Pseudo-code: guided state feeds existing initializers/forms, never a second document model -> retain entered content when leaving -> blockers point to package/criterion recovery -> Save/check uses shared operations. Experts can exit to normal editor at each durable stage.
- Tests: first voltage test create/save/reopen/complete through app, equivalent guided/direct documents, leave/resume retains inputs, actionable blockers and explicit demo distinction.
- Risks/conflicts: lifecycle/wizard ownership. Out of scope: automatic bench deployment.

## 22 Expert commands and saved layouts

- Depends on: 11/15–18; expose19–20 commands. Files: command catalog/palette/shortcuts, versioned preferences/layout, lifecycle/TUI reconciliation hooks.
- Surface: search commands and platform shortcuts for save/history/rename/duplicate/add/move/next issue/new plan/workspace; prerequisite explanations; collapsible rail, wide preview dock, Issues drawer, dependency summary, remember/reset layout.
- Pseudo-code: focused control gets normal text shortcuts first -> applicable editor command -> shared transaction; save local future-safe preferences -> clamp restored layout to viewport; async picker/chooser captures owner+workspace generation -> after await recheck before Save/Discard/open; TUI escape hatch checks prerequisites and uses06 reconciliation on return.
- Tests: text Undo/Redo unaffected, keyboard workflows incl initialization, smaller restored screen, future preference bytes, owner hidden/session changed pending choices, Linux native picker/close where available; document Windows native validation still required if host unavailable.
- Risks/conflicts: focus and OS-specific behavior. Out of scope: alternate document/runtime implementations.

## Final manual review handoff

Publish the top branch without merging the stack. Supply the PR chain and exact checkout/build/run command; verify example and product routes, incomplete-save/recovery, typed bindings, loop/duplicate/history, preview provenance, issue navigation, package preparation/build/cancel/receipt, initialization, keyboard and narrow-window behavior. Record actual automated/native coverage and any unavailable environment prerequisites. Summarize unapplied useful nits by PR; do not describe planned acceptance as proven execution.
