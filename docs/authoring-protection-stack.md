# Authoring protection implementation stack

Execution base: 1e8cf62 (latest). The solution file was retired; use dirs.proj and the CI task runner. This stack delivers combined-plan packages 01–04. Follow-on milestones remain planned.

Goal: authoring users can exercise UI regressions without hardware, package only saved and checked workspace content, retain edited programs through saves/navigation, and understand the scope of destructive edits.

Stack: latest <- fix/traversal-test-discovery <- feat/authoring-ui-tests <- feat/authoring-pack-protection <- feat/authoring-save-lifecycle <- feat/authoring-destructive-scope. Do not merge these PRs.

## Prerequisite Traversal discovery repair
- Evidence: upstream PR #182 removes HardwareTest.slnx, but architecture/authoring test root helpers and bootstrap package discovery still require it. Both upstream CI test jobs fail at architecture smoke.
- Goal: restore required checks after the upstream traversal migration without recreating a solution or weakening coverage.
- Files: test root helpers; ArchitectureRulesTests traversal contract; minimal OpenTapHomeBootstrapper root marker repair.
- Pseudo-code: walk parents to dirs.proj; assert traversal SDK and src/tests globs; compare independent source/test project discovery excluding bin/obj against existing traversal expansion; preserve runtime lock graph checks. Use dirs.proj for in-tree bootstrap package discovery.
- Tests: architecture and authoring suites; format changed projects. Published bootstrap portability is Area 2.
- Conflicts: test helpers only; Area 1 uses output-linked fixtures. UI branch rebases onto reviewed prerequisite before publishing.

## Area 1 Authoring UI regression foundation
- Goal: dedicated headless authoring tests and isolated deterministic fixtures for future protection flows.
- Depends on: traversal-test-discovery prerequisite.
- Out of scope: packaging, lifecycle or destructive behavior changes; recreating retired solution file.
- Files: new tests/HardwareTest.Authoring.UI.Tests; tools/ci/main.ts and deno.json; .github/workflows/ci.yml; docs/testing.md; narrowly necessary authoring testability seams.
- Surface: separate Avalonia application fixture; isolated workspace/preferences fixture; injectable interaction abstractions only if needed for tests, with real behavior unchanged.
- Pseudo-code: Build a headless Application with Fluent styles/Inter and initialize MainWindow(viewModel) directly; copy sample or create minimal workspace in unique temp directory; pass explicit preference store; show window; drive controls using actual input/routed events; dispatcher drains; assert empty/loaded/edit states and selected data/context; dispose windows and fixture directories. Add a required test:authoring-ui task wired through all, task catalogs and both Windows/Linux workflow steps. Do not apply existing advisory operator E2E behavior to authoring tests.
- Tests: empty workspace opening action visible/editor hidden; populated workspace bindings load without recursion; edit reaches VM and unsaved status; independent preference roots; normal/minimum width render layout; CLI exits before Avalonia remains covered by existing core suite.
- Risks: App initialization writes global prefs; assembly Avalonia app collision; headless tests fail to prove real control behavior; new task catalog must stay synchronized.
- Conflicts: CI/task docs with later test additions; no packaging core changes.

## Area 2 Packaging protection
- Goal: no dirty GUI pack or nullable production compatibility bypass; configured environment selected consistently, preflight results are actionable and artifact inclusion visible.
- Depends on: Area 1.
- Out of scope: full immutable build snapshots or subprocess pipeline (packages 09/10); durable draft format; save/navigation guards.
- Files: AuthoringWorkspaceViewModel pack method (prefer new Pack partial); WorkspacePacker, AuthoringCli, WorkspacePackPlan/Ship; MainWindow ship controls; core+headless packaging tests; publish/verify CI support and runtime package manifest content.
- Surface: `PackPreflightFinding(Code, Message, IsError, Path?)` and `PackPreflightReport(Findings, Contract?, Compatibility?)` expose retained details and `HasErrors`; `WorkspacePacker.Preflight(workspace, options)` returns findings before artifact writes; `Pack` always calls it. Add a report-carrying pack exception or equivalent explicit result without erasing existing code prefixes. The VM exposes `LastPackPreflight`, `CanPack`, and read-only dirty program IDs. Keep `PackOptions.Compat` as an optional injected test checker, but null resolves to real `TuiCompatChecker`, never a skip. Preserve CLI options and explicit Home/TuiHome precedence; VM resolves preferences when Home is omitted.
- Pseudo-code: if no writable workspace -> structured failure; if any dirty program -> error identifying IDs, before touching output/home; resolve supplied Home or prefs override/default; require checker defaulting to real TuiCompatChecker for production, with explicit fake injection available to tests; inspect prerequisites and strict contract, compare catalogs/roundtrip, fail with retained findings on blocker; then create artifacts. Avoid marking a missing required compatibility package as passed. Show pack inclusion/exclusion from actual package file selection. Respect includeTui=false with accurately labeled catalog/roundtrip checks; require an installed TUI package when includeTui=true rather than comparing an empty environment to itself. Publish authoring separately and run headless startup/validate/bootstrap smoke from an isolated directory with no repository ancestry. Bundle Basic/Mixins package manifests so bootstrap works outside a checkout. Guard direct packer and GUI to avoid caller omission bypass. Create output only after preflight succeeds; existing artifact paths remain untouched on preflight failure.
- Tests: dirty edit/new program block before outputs; custom configured home consumed by GUI/default options; checker executed by direct and CLI paths; blocking checker stops package creation; missing prerequisites fail visibly (includeTui=false must remain supported; includeTui=true requires evidence of an installed TUI package); strict failure retains details; inclusion list agrees with PackageXmlRenderer; existing offline pack tests explicitly inject deterministic compatibility fixtures where appropriate.
- Risks: current checker is catalog/roundtrip not a TUI process; don't claim installed real TUI coverage without package evidence; default sample package inclusion isn't all programs; semantic changes must not be mixed here.
- Conflicts: VM/MainWindow shared with Area 3, so wait for reviewed Area 2 before implementing Area 3.

## Area 3 Saving and lifecycle
- Goal: per-program dirty state, Save all results, preservation of edited session/selection, cancel-safe reopen/close.
- Depends on: reviewed Area 2.
- Out of scope: durable invalid drafts, undo/node IDs, asynchronous build.
- Files: new focused AuthoringWorkspaceViewModel Saving partial; necessary core refactors; new lifecycle interaction coordinator/window partial; Programs rows and toolbar; core/headless tests.
- Surface: `DirtyProgramSummary(PlanId, PlanDirty, SidecarDirty)`; `SaveAllResult(SavedProgramIds, Failures)` with `Succeeded` only when no unsaved state remains; a focused saving partial implements `SaveProgram(planId)` and `SaveAll()`, with existing Apply/SaveSidecar delegating compatible semantics. `UnsavedChangesChoice` = SaveAll/Discard/Cancel and `IAuthoringLifecycleInteraction.ChooseAsync(dirtySummary)` is implemented by a real modal and injected test interactions. `MainWindow` open/close entrypoints call one coordinator with an in-flight guard. A prospective-load API must validate into temporary session data, commit only after successful load, and require an explicit discard intent when dirty.
- Pseudo-code: collect dirty IDs; save each applicable program with existing compiler without reopening entire workspace; update paths and clear only successfully saved flags; leave failed drafts dirty, preserve selection/sequence context; report every failure. For navigation/closing, commit focused editor before evaluating dirty state; prompt once when dirty, Cancel => no action, SaveAll => continue only if fully saved, Discard => load replacement only after load succeeds. Load prospective workspace into temporary state before replacing current session. Window Closing cancels initial event, asynchronously chooses, then reissues close once allowed; prevent concurrent/reentrant prompts. Keep direct Open refusing dirty replacement; private load path used for known internal refresh only.
- Tests: two program save-one/save-all/partial failure; sidecar-only edits distinct from plan edits; unknown/new program paths; focus/sequence preservation; cancel picker/dialog, close/reopen, invalid workspace retains drafts; read-only cannot silently discard; concurrent lifecycle requests don't trigger duplicate prompts.
- Risks: mutable sidecars/cached listbox selection; apply currently reloads Open; save-all errors must not clear unrelated flags; LostFocus changes must precede guard.
- Conflicts: shares VM/window with Areas 2/4; implement serially, test fixtures from Area 1.

## Area 4 Destructive scope
- Goal: per-program membership operations cannot masquerade as workspace deletion; explicit replacement impact and consistently deferred saving.
- Depends on: reviewed Area 3.
- Out of scope: full edit-history model, instrument adapter/type discovery; unknown compatibility remains conservative.
- Files: CatalogDelete partial, AuthoringInstrumentUsage, ProgramSettingsView/events; focused confirmation dialog/coordinator; core/headless tests.
- Surface: stable impact description enumerating affected programs/nodes and scope; explicit instrument replacement request; confirm target/version before applying.
- Pseudo-code: toggle per-program inclusion only updates selected draft; workspace delete computes impact, requires named confirmation, checks impact hasn't changed, updates in-memory catalogs and affected drafts, marks scope dirty, persists only through common Save/SaveAll. Instrument remove requires chosen compatible replacement among same supported type where usage remains; workspace catalog changes are tracked independently and saved by SaveAll, never an unrelated program Save; raw/opaque refs block; show identity/measure/cleanup impact; applying updates draft only and marks dirty. Cancel leaves files/drafts unchanged. Never choose first remaining slot or implicit compiler save.
- Tests: membership A doesn't modify B; scoped delete impact and cancellation; no sidecar/manifest writes before explicit save; saved catalogs reload; incompatible/different type/last slot/raw refs blocked; replacement choices update all known references and cleanup policy; stale confirmed target doesn't apply.
- Risks: existing setters persist catalogs immediately; staged workspace changes need dirty/save accounting beyond per-program flags. Full compatibility adapter model stays in package 08; use current exact known type compatibility conservatively.
- Conflicts: VM/catalog/window/saving integration with Area 3, start after review.

## Review and nit handling
Implementation agents receive only their area spec. Independent reviewers get the area contract, base/head, changed files and reduced-context review prompt. Findings are tracked as Must fix / Should fix / Nit / Follow-up. Fix Must and in-scope Should then re-review; assign later-area issues to plan packages. Record useful unapplied nits and rationale. Existing style-only nits are omitted. CI and human PR reviews share the same queue. Keep no more than two non-conflicting areas in flight and rebase children after parent fixes.

