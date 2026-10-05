# Authoring design feedback follow-up

## Goal

Replace the crowded toolbar-led editor with a clear workspace landing page and focused authoring shell. New plan hardware choices come from supported Instrument Components devices rather than a hard-coded DMM menu. Explain setup as the actions run before measurements and cleanup as the actions run afterward. Reuse installed, compatible packages; make missing prerequisites recoverable without requiring routine reinstallation.

## Stack

`feat/authoring-expert-commands` ← `feat/authoring-design-refresh` ← `feat/authoring-library-devices` ← `feat/authoring-workspace-view-design`.
Do not merge these PRs. Implement in order. Area 2 may overlap Area 1's pushed review only if it does not modify the shell or shared styles; pause and rebase if a parent finding affects it. Area 3 follows Area 2 because hardware and initialization views share surfaces.

### Area 1: Focused visual shell
- Goal: Prominent central Create workspace action; compact header and deliberate navigation; distinct sequence and inspector surfaces; a command search field separated from padded results. Commands text is vertically centered.
- Depends on: Completed authoring stack.
- Out of scope: Hardware adapter and compiler changes; save/lifecycle semantics.
- Likely files: MainWindow.axaml, MainWindow.Shell.cs, App.axaml/styles, ProgramsRailView, SequenceEditorView, selected inspector, command palette.
- Public surface: Existing commands and route identities remain accessible. Primary actions are Save all, Check, Commands; specialized actions remain available through menus/palette or their relevant view. All owner/session guards remain intact.
- Pseudo-code: No workspace -> centered welcome with primary Create, secondary Open, recent workspace. Workspace -> compact identity/action header + navigation + contained editor cards. Preserve named route controls and focus restoration. Palette -> search header + separated scrollable command results with bottom inset + action footer. Layout clamps at 960×600 and larger text.
- Tests: Existing headless routing/lifecycle/command tests; meaningful visibility/interaction checks where hierarchy changes; actual native welcome/editor/palette screenshots at normal/minimum size.
- Risks: Hidden specialized actions, focus/automation regressions, reduced viewport. Preserve command catalog and UI names where possible.
- Conflicts with: Area 2 reads shared styles but owns wizard/Core files. Overlap only after this area is pushed and only while shared shell/styles stay unchanged.

### Area 2: Library hardware and explicit plan lifecycle
- Goal: New plan offers supported library device types dynamically and preserves selected type/configuration through save/reopen. Setup and cleanup describe the resulting plan actions. Compatible installed packages are reused.
- Depends on: Area 1.
- Out of scope: Inventing measurement capabilities for arbitrary devices, executing hardware in the authoring process, silently substituting mocks, runtime plugin replacement.
- Likely files: Instrument catalog/adapters, plugin catalog inspection, plan initialization window/retained guided state, initializer/compiler tests, readiness/preparation UI.
- Public surface: Stable descriptors for installed library devices with actual type/package/capabilities; choices store descriptor/resource identity rather than positional DMM indices. Missing library displays actionable preparation guidance. Demo devices explicitly labeled.
- Pseudo-code: Inspect supported Instrument Components metadata -> produce safe device descriptors -> combine with existing/reusable resources -> selected descriptor builds typed binding -> validate recipe against real capabilities -> save source and compile with exact type. Unsupported devices remain unavailable with reason; no generic DMM lowering. Retained wizard input uses type identity. Readiness checks installed payload/version; offer preparation only for missing/incompatible packages.
- Tests: Multiple real library device options, typed roundtrip and no DMM substitution, missing/unknown capability rejection, retained selection, reuse installed compatible payload, wizard setup/cleanup copy and interaction. Actual library source/API inspection precedes implementation.
- Risks: Library package availability and APIs, serializer/import contracts, VISA loading safety. Keep native I/O outside authoring process.
- Conflicts with: Area 1 shell/styles if changed; pause the child, fix and rebase when a parent finding affects those contracts. Area 3 owns these hardware/form surfaces afterward.

### Area 3: Consistent workspace and form design
- Goal: Carry the redesign through the workspace routes and supporting forms, rather than leaving old flat screens inside the new shell. Engineers see a clear task, state, and primary action in each view.
- Depends on: Area 2.
- Out of scope: New document models, hardware execution, changes to compilation/validation/build receipts, new packages, and changes to shared operator widget semantics.
- Likely files: ProgramSettingsView, HardwareView, WorkspaceDefinitionsView, WorkspaceIssuesView, WorkspaceEnvironmentView, WorkspaceBuildView, WorkspacePreviewView, selected-step form presentation, WorkspaceCreationWindow, SettingsView and their layout helpers.
- Public surface: Existing bindings, commands, automation names, issue targets and lifecycle guards. Shared section headings, contained list/detail surfaces, and clear primary/secondary actions with light/dark resources.
- Pseudo-code: Route -> concise heading/purpose -> task actions -> scrollable sections with visible scope. Hardware separates program requirements from typed instrument bindings; Definitions distinguishes workspace catalog from program membership. Environment separates selected home, package requirements, and preparation/import actions. Build separates included saved plans/readiness, build action, and previous receipt/history, retaining truthful stale/error state. Issues preserves severity/provenance and direct field navigation in clear rows. Preview separates data provenance and recording actions from the shared operator board. Supporting forms group fields by task with measured scrolling bodies and visible decision footers. Preserve focus restoration and all incomplete/unknown values; do not replace existing domain controls with a second model.
- Tests: Existing route/form/issue/build/preview UI behavior tests; actual native screenshots of every redesigned route at normal/minimum sizes, including long content and dark theme. Verify primary actions and form decision controls stay reachable with larger text.
- Risks: Lost scope/provenance, hidden controls, clipped rows, focus regressions. Keep automation and binding contracts and use production styles in headless tests.
- Conflicts with: Area 2 hardware and wizard surfaces; implement after its review loop.

## Review

After each push, run a fresh reduced-context review for the complete area, fix Must/Should findings and repeat. Record verification and useful remaining nits by PR.
