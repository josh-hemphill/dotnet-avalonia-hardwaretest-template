# Authoring UI review remediation stack

## Goal

Engineers can edit and validate a program with a clear account of which state is saved, find and correct contract problems, and use the editor at its supported window size without accidental loss of a program.

## Stack

`latest` ← `authoring-ui-delete` ← `authoring-ui-draft` ← `authoring-ui-findings` ← `authoring-ui-actions` ← `authoring-ui-layout`.
Each branch targets its immediate predecessor. Merge from left to right.

### Area 1: Confirm program deletion
- Goal: Both Remove button and Delete key require confirmation naming the program and its disk files.
- Depends on: `latest`.
- Out of scope: Sequence deletion, undo, changing deletion semantics.
- Likely files: `MainWindow.axaml.cs`, `AuthoringWorkspaceViewModel.cs`, authoring tests.
- Public surface: A view-model description of the pending deletion; a modal confirmation in the window.
- Pseudo-code: `DescribeSelectedProgramRemoval()` returns plan ID and existing TapPlan/sidecar paths or fails when removal is unavailable. `RequestRemoveProgramAsync()` reads the description, shows a modal confirmation with Cancel as default, then calls `RemoveSelectedProgram()` only on explicit confirmation. Route button and keyboard handlers through this method. Recheck selection before deletion if it changed while dialog was open.
- Tests: Description identifies program and files; cancelled dialog path leaves files untouched where UI testing is practical.
- Risks: Dialog API and window closing while open.
- Conflicts with: Area 4 in `MainWindow.axaml.cs`.

### Area 2: Draft state and validation scope
- Goal: Visible unsaved state; validation cannot be mistaken for validation of edited but unsaved data.
- Depends on: Area 1.
- Out of scope: New compiler or contract validator.
- Likely files: `AuthoringWorkspaceViewModel*.cs`, `MainWindow.axaml`, tests.
- Public surface: `HasUnsavedChanges`, validation scope text and save state.
- Pseudo-code: Track changes to sidecar and plan draft separately. On edits mark current program dirty; on save clear the corresponding flag, on workspace load reset flags. If any unsaved draft exists, `Validate` must either save/compile first or report that validation requires saving; the UI exposes that state and disables/makes scope explicit. Keep findings from previous validation visibly stale or clear them after an edit.
- Tests: Change a field, validate, save and validate; switch programs; sidecar-only save does not clear plan edits.
- Risks: Numerous setter paths; avoid false clean state.
- Conflicts with: Areas 3 and 4 in view model and XAML.

### Area 3: Actionable findings
- Goal: Each finding identifies program and location, with navigation where a target can be resolved.
- Depends on: Area 2.
- Out of scope: Changing validation rules.
- Likely files: `AuthoringWorkspaceViewModel.cs`, `MainWindow.axaml`, tests.
- Public surface: `AuthoringFindingRow` with severity, program, location, message and optional target.
- Pseudo-code: Project validator plan reports into rows retaining the source plan ID and finding metadata. Bind list to rows. On activation, select matching program and sequence or field only when a reliable target exists; otherwise show location text without misleading navigation.
- Tests: Multiple program findings retain provenance; targetless finding stays readable.
- Risks: Validator may not expose machine-readable paths for every rule.
- Conflicts with: Area 2 validation state, Area 4 XAML.

### Area 4: Action hierarchy and save scope
- Goal: The main toolbar visually distinguishes workspace setup, editing, and validation, and explains both save actions without relying on tooltips.
- Depends on: Area 3.
- Out of scope: Changing save behavior.
- Likely files: `MainWindow.axaml`, UI copy tests.
- Public surface: Grouped controls and short visible scope labels.
- Pseudo-code: Recompose header into labeled groups. Keep keyboard order logical. Promote Save plan as primary action; retain Save sidecar with visible sidecar-only description. Place destructive Remove next to program list instead of general toolbar.
- Tests: XAML loads; automation names remain distinct.
- Risks: Header height at minimum size.
- Conflicts with: Areas 1 and 5 in XAML.

### Area 5: Minimum-size layout
- Goal: Sequence, inspector, and preview remain usable at supported minimum size.
- Depends on: Area 4.
- Out of scope: Rebuilding preview widgets.
- Likely files: `MainWindow.axaml`.
- Public surface: Resizable panes and/or preview tab at narrow width.
- Pseudo-code: Allocate flexible columns with practical minima and splitters, or move preview to its own tab. Verify at 960×600 and normal size with rendered app if available.
- Tests: XAML load; manual visual inspection and keyboard traversal.
- Risks: Preview width and scroll behavior.
- Conflicts with: Area 4 XAML.

## Conflict map

Areas 1, 4, and 5 edit `MainWindow.axaml` or code-behind. Areas 2 and 3 share validation state in the view model. Review and fix each parent branch before beginning its conflicting child; rebase children if parent fixes land later.
