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

Observed on clean local build checkpoint `d5297f96bdb4b92ead357731c560a743a7952dd7`, tree `9bc88a61886ac1af08995cf09d7a3d5988b4fec7`. Later changes to this checkpoint are test corrections and documentation only; retain that distinction when reading the final PR head. Actual rebuilt authoring executable, corrected fixture copies, private writable preferences/cache and the same 1280×800 dummy Xorg display were used.

- Ctrl+O opened the actual GTK workspace picker; Escape cancelled it. Explicit application focus was used because the display has no window manager; automatic desktop focus restoration remains unverified.
- Ctrl+Shift+P opened Commands, and searching Toggle Programs rail then pressing Enter collapsed the entire rail. The local layout persisted across reopening.
- Editing repeat 3 and sending an OS close event opened the actual Unsaved programs dialog. Cancel retained the dirty app and all six original fixture files stayed byte-identical. Discard then closed the app with exit 0, again preserving all six original files.
- On another corrected fixture copy, the observer verified rendered repeat 4 and Unsaved changes before choosing OS close/Save all. The app exited 0; saved source and compiled TapPlan both contained repeat 4, with requiresCompilation=false. Reopening restored 4 without unsaved changes, then a clean OS close exited 0. Unlike the initial fixture observation above, matching prepared hashes now allow this workspace to save and compile normally.
- An automated numeric-field Ctrl+Z snapshot still showed 3; it is not counted as a passing native numeric undo check. Automated text-control shortcut tests provide separate evidence. Native numeric-field focus/undo deserves focused manual follow-up; the snapshot alone does not establish a regression or its cause.

The first native harness attempt tried to focus the window before X11 had mapped it; the harness was corrected to wait for IsViewable. One later automated Save All attempt never made a document edit and was not counted. The successful Save All observation above used a visibly dirty edited field. These harness issues did not change the product or weaken its tests.
