# Observed Linux desktop checks

Observed on PR209 initial head `836c96ca60da6c905534c19cbe3e084c6c03b1ce`, source tree `0879ebe5ebed4ee06223ac06f207e28f9519e713`. The rebuilt app and published authoring executable used this exact source tree. Tests used writable copies of the compiled pass fixture and private workstation preferences.

A 1280×800 dummy Xorg display with XTest keyboard/mouse input and OS close events exercised actual native windows. There was no window manager, normal desktop session, engineer participant or physical bench. These observations are distinct from headless tests and prepared review inputs.

- Ctrl+O opened the GTK workspace picker, and Escape cancelled it without source changes. With no window manager, focus returned to the root display; the observer explicitly focused the app before continuing. Automatic normal-desktop focus restoration remains unverified.
- Ctrl+Shift+P opened Commands. Searching Toggle Programs rail and pressing Enter collapsed both the Programs list and guidance controls; the local layout persisted after reopening.
- In the focused repeat-count text input, select-all/type3/Ctrl+Z restored2, preserving normal text undo in this case.
- An edited repeat3 triggered the Unsaved programs dialog through an OS close event. Cancel retained the dirty app; all six original fixture files remained byte-identical. The separate recovery checkpoint did not count as a source save.
- A second OS close followed by Discard exited successfully and left all six original fixture files unchanged.
- On a fresh fixture copy, repeat4 followed by OS close/Save all persisted the source draft and exited successfully. The source explicitly required compilation; the existing compiled repeat2 remained intact. Reopening the published app restored4, and Build reported Saved source revision1 / Saved source requires compilation / No completed checked build. This is draft-save evidence, not successful compilation of the exploratory chained-mean fixture.
- The reopened clean app accepted an OS close event and exited successfully.

Remaining review includes normal window-manager focus restoration, native Windows/macOS picker/close/keyboard behavior, additional sizes/scaling/themes/accessibility routes, engineer usability workflows and physical bench validation. Prepared fixtures and automated results must not be reported as those manual sessions.

## Repeated checks after the review fixes

Observed on clean local build checkpoint `d5297f96bdb4b92ead357731c560a743a7952dd7`, tree `9bc88a61886ac1af08995cf09d7a3d5988b4fec7`. The first follow-up checkpoint changed only tests and documentation. Subsequent review fixes add initiating owner/VM/session checks for operation cleanup, destructive dialogs and preview actions, retain operation reuse after a stale close is cancelled, recognize immutable workspace updates within the same session after first compilation, resume recovery after an aborted close once its old writers drain, tolerate missing optional Unix stop markers during owned reaping, and replace Windows command-interpreter launching with direct console-executable launching. These observations describe the d529 build; they do not establish native Windows behavior at the later head. Actual rebuilt authoring executable, corrected fixture copies, private writable preferences/cache and the same 1280×800 dummy Xorg display were used.

- Ctrl+O opened the actual GTK workspace picker; Escape cancelled it. Explicit application focus was used because the display has no window manager; automatic desktop focus restoration remains unverified.
- Ctrl+Shift+P opened Commands, and searching Toggle Programs rail then pressing Enter collapsed the entire rail. The local layout persisted across reopening.
- Editing repeat 3 and sending an OS close event opened the actual Unsaved programs dialog. Cancel retained the dirty app and all six original fixture files stayed byte-identical. Discard then closed the app with exit 0, again preserving all six original files.
- On another corrected fixture copy, the observer verified rendered repeat 4 and Unsaved changes before choosing OS close/Save all. The app exited 0; saved source and compiled TapPlan both contained repeat 4, with requiresCompilation=false. Reopening restored 4 without unsaved changes, then a clean OS close exited 0. Unlike the initial fixture observation above, matching prepared hashes now allow this workspace to save and compile normally.
- An automated numeric-field Ctrl+Z snapshot still showed 3; it is not counted as a passing native numeric undo check. Automated text-control shortcut tests provide separate evidence. Native numeric-field focus/undo deserves focused manual follow-up; the snapshot alone does not establish a regression or its cause.

The first native harness attempt tried to focus the window before X11 had mapped it; the harness was corrected to wait for IsViewable. One later automated Save All attempt never made a document edit and was not counted. The successful Save All observation above used a visibly dirty edited field. These harness issues did not change the product or weaken its tests.

### Current product native desktop verification

On 2026-10-05, the Linux authoring publish built from product checkpoint `2c00274be8ed8f8739f135338af1b109edd7379f` (tree `9f491276dabf77c1017919878691c3a6ab70c655`) was exercised on an actual X11 display. This checkpoint includes recovery resumption after aborted close and retained Unix process reaping after control-marker write failure. The following final documentation-only commit does not change these product bytes.

Fresh copies of the compiled pass fixture were used. Ctrl+O opened the actual GTK workspace picker; explicitly focusing it and pressing Escape canceled it. Ctrl+Shift+P opened the actual command palette; filtering “Toggle Programs rail” and pressing Enter collapsed the entire Programs rail. Filtering “Reset saved layout” and pressing Enter restored it.

A visible repeat-count edit from 2 to 3 produced both an unsaved indicator and a recovery checkpoint. An operating-system close request opened the actual dirty chooser. Cancel retained the app, and all six original source/compiled fixture files retained their original SHA256 values. A second close followed by Discard exited successfully, with all six original hashes still unchanged.

A second fresh fixture was visibly edited from 2 to 4 repeats. Operating-system close followed by Save all exited successfully. The persisted authoring source has repeat count 4 and `requiresCompilation: false`; the compiled TapPlan has `<Count>4</Count>`. Reopening that exact workspace visibly showed repeat count 4 without an unsaved indicator; a clean close exited successfully. All three app processes exited with code 0.

The test display has no window manager, so dialog focus was set explicitly. These observations establish actual Linux picker, palette, dirty-close, compilation, and reopen behavior. Ordinary window-manager focus, numeric text Ctrl+Z, Windows/macOS native interaction, engineer manual acceptance, and physical bench operation remain unverified; they are not claimed as passed.
