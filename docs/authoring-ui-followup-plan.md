# Authoring UI follow-up stack

## Goal

The sample workspace opens reliably, and the empty and populated editor states tell engineers what to do next without repeated guidance. The remaining visual review items are verified in the running app.

## Stack

`latest` ← `fix/authoring-ui-launch` ← `fix/authoring-ui-empty-state` ← `fix/authoring-ui-inspector-copy`.
Each PR targets its predecessor. The five already merged authoring UI PRs cover draft state, findings provenance/navigation, action hierarchy, deletion safety, and minimum-width layout.

### Area 1: Workspace launch
- Goal: Opening `plans/opentap` does not recurse in Avalonia ComboBox binding.
- Depends on: `latest`.
- Out of scope: UI layout and copy.
- Likely files: `AuthoringWorkspaceViewModel.ProgramSettings.cs`, authoring UI tests.
- Public surface: Stable `YUnitOptions` and `MetricFunctionChoices` collections and selected item identity for ComboBox.
- Pseudo-code: Cache each choice list by its ordered values; return the same collection and selected object while values are unchanged. Refresh the cache when the selected metric or catalog changes. Exercise MainWindow with a populated workspace in a desktop smoke run.
- Tests: Open the sample workspace in the rendered app; cover stable collection and selected item identity in a unit test.
- Risks: Stale choices when catalog or selection changes.
- Conflicts with: None of the later areas beyond shared test setup.

### Area 2: Empty workspace state
- Goal: A newly opened app clearly directs the engineer to open a workspace and keeps editing actions unavailable until one exists.
- Depends on: Area 1.
- Out of scope: Changing workspace load or save semantics.
- Likely files: `MainWindow.axaml`, view model state, authoring UI tests.
- Public surface: `HasWorkspace` and visible empty-state panel.
- Pseudo-code: Derive `HasWorkspace` from `Workspace`; show a central opening instruction while false; hide the editor tabs and disable actions that require a selected program. Raise state change when workspace opens.
- Tests: Empty and loaded states; rendered screenshot at normal and minimum window sizes.
- Risks: Hiding tabs must not remove keyboard access to workspace opening.
- Conflicts with: Area 3 in `MainWindow.axaml`.

### Area 3: Inspector guidance and visual check
- Goal: The inspector purpose appears once when no step is selected, and selected-step context remains clear.
- Depends on: Area 2.
- Out of scope: Reworking the three-pane editor.
- Likely files: `MainWindow.axaml`, `AuthoringWorkspaceViewModel.Sequence.cs`, tests.
- Public surface: Inspector breadcrumb text.
- Pseudo-code: Show purpose near the heading, use a distinct empty-selection instruction in the inspector body, and keep the selected section/step breadcrumb. Inspect normal and minimum window sizes with sample program data.
- Tests: View-model copy test and manual rendered review.
- Risks: Long step names may wrap.

## Conflict map

Areas 2 and 3 both edit `MainWindow.axaml`, so complete Area 2 review before Area 3. Area 1 changes only function-choice binding state and a test. Each child branch builds on the reviewed parent branch.

## Area 1 desktop smoke evidence

On Windows with .NET 10, `dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- plans/opentap` opened the populated editor. The Programs rail showed Board Demo and Sample Hardware Suite; Board Demo's Acquire 3V3 metric was selected, its Y unit and measure recipe ComboBoxes rendered, and the timeseries preview chart rendered. The process remained open without stderr output until manually closed. This exercises the ComboBox binding path that previously produced a stack overflow at startup.

## Area 2 desktop smoke evidence

Launched without a workspace on Windows at 1280×800 and resized to 960×600. The welcome panel and both workspace-opening actions remained visible at both sizes; Bootstrap, Save, and Validate were disabled and the editor was hidden. The validation-scope message is hidden until a workspace is open.

## Area 3 visual check

Launched the populated sample workspace at 1280×800 and 960×600. At minimum size, the selected Acquire 3V3 step, channel key, display role, Y unit, instrument slot, and preview chart remained visible. The inspector's empty-selection body now gives the next action instead of repeating its purpose line. The three-pane view is compact at 960×600; further density changes are outside this copy fix.
