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
- Implementation plan (upstream `7e1a771`, inspected before changes): selected-home metadata, payload containment and compatible package assessment grant discovery; load the supported assembly and enumerate public concrete `ScpiInstrument` types with OpenTAP Display metadata. Resolve genuine archive home-root DLLs and unpacked package-relative DLLs using declared file hashes; reject ambiguous or unsafe layouts. Verify module identity when first recording immutable library/contract fingerprints; match selected payload bytes against those fingerprints to reject foreign cached types even after origin-home modification/deletion. Different healthy installed bytes request an authoring restart. Cache descriptors by exact TypeId; editor options are stable and selected-home-specific.
- Typed binding plan: preserve exact TypeId/address/timeout through source and compiled resources. Library setup uses `IdentityQueryStep`, cleanup uses `SafeShutdownStep` (output off, then reset), preserving identity node and named slot membership. Keep legacy Basic/VISA resources explicit. Library devices deliberately have no Basic voltage/mean capability; incomplete tasks report incompatibility instead of lowering to a DMM or claiming mean parity.
- Recovery plan: wizard leaves for Environment with raw inputs and stable type/resource identity retained; Environment explicitly stages a required library dependency through workspace history, Undo and Save All before trusted offline import/preparation. Resume New test plan restores the form. Compatible installed payload is reused; missing metadata/payload guidance names the fault and route. Product defaults declare Instrument Components and visibly offer a Product hardware scaffold with no measurement recipe (previously a Product voltage task with Basic acquisition). The legacy ProductVoltage enum and explicit IncludeVisaPackage API remain compatible.
- Tests: Multiple real library device options, typed roundtrip and no DMM substitution, missing/unknown capability rejection, retained selection, reuse installed compatible payload, wizard setup/cleanup copy and interaction. Actual library source/API inspection precedes implementation.
- Verification: Built the actual upstream `InstrumentComponents.OpenTap` net8.0 package at `7e1a771` outside the product tree; real-assembly tests discover all eight types and roundtrip a DC power supply with address, 8123 ms timeout, identity and cleanup bindings. Actual Release offline bootstrap reuses both library DLLs and original package metadata byte-for-byte, with no legacy VISA plugin. A genuine upstream offline TapPackage created with OpenTAP normal packaging actions also passes fresh-process actual import, eight-device/non-DMM lifecycle, stale-layout rejection and compatible reuse/repair integration. Native Xorg captures cover the real eight-choice popup, physical resource readiness, and setup/cleanup at normal and minimum sizes. No hardware I/O was performed.
- Import recovery: Unavailable library and unknown vendor resources are sanitized together and retain exact source XML, type/address and configuration. A fresh production-compiler process with no resident library preserves cold identity/shutdown XML and node/binding references as opaque source, blocks compilation even after package installation, and directs Environment preparation followed by reimport of the original compiled plan. Reimport restores the original setup/cleanup phase identities; opaque lifecycle rows cannot silently become measurement actions.
- Risks: Library package availability and APIs, serializer/import contracts, VISA loading safety. Keep native I/O outside authoring process.
- Conflicts with: Area 1 shell/styles if changed; pause the child, fix and rebase when a parent finding affects those contracts. Area 3 owns these hardware/form surfaces afterward.

### Area 3: Consistent workspace and form design
- Goal: Carry the redesign through the workspace routes and supporting forms, rather than leaving old flat screens inside the new shell. Engineers see a clear task, state, and primary action in each view.
- Depends on: Area 2.
- Out of scope: New document models, hardware execution, changes to compilation/validation/build receipts, new packages, and changes to shared operator widget semantics.
- Likely files: ProgramSettingsView, HardwareView, WorkspaceDefinitionsView, WorkspaceIssuesView, WorkspaceEnvironmentView, WorkspaceBuildView, WorkspacePreviewView, selected-step form presentation, WorkspaceCreationWindow, PlanInitializationWindow and its presentation/layout helpers, SettingsView and their layout helpers. Preserve Area 2 hardware type/input identity and functional lifecycle/recovery copy; these form changes are presentation only. Follow up the untouched default logical slot `DMM` for new non-DMM devices with a generic suggested name while preserving intentional, reusable and retained names.
- Public surface: Existing bindings, commands, automation names, issue targets and lifecycle guards. Shared section headings, contained list/detail surfaces, and clear primary/secondary actions with light/dark resources.
- Pseudo-code: Route -> concise heading/purpose -> task actions -> scrollable sections with visible scope. Hardware separates program requirements from typed instrument bindings; Definitions distinguishes workspace catalog from program membership. Environment separates selected home, package requirements, and preparation/import actions. Build separates included saved plans/readiness, build action, and previous receipt/history, retaining truthful stale/error state. Issues preserves severity/provenance and direct field navigation in clear rows. Preview separates data provenance and recording actions from the shared operator board. Supporting forms group fields by task with measured scrolling bodies and visible decision footers. Preserve focus restoration and all incomplete/unknown values; do not replace existing domain controls with a second model.
- Tests: Existing route/form/issue/build/preview UI behavior tests; actual native screenshots of every redesigned route at normal/minimum sizes, including long content and dark theme. Verify primary actions and form decision controls stay reachable with larger text.
- Risks: Lost scope/provenance, hidden controls, clipped rows, focus regressions. Keep automation and binding contracts and use production styles in headless tests.
- Conflicts with: Area 2 hardware and wizard surfaces; implement after its review loop.

## Review

After each push, run a fresh reduced-context review for the complete area, fix Must/Should findings and repeat. Record verification and useful remaining nits by PR.
