# Prepared authoring review inputs

These UTF-8 fixtures are deterministic mock/test data, prepared for review. They are not evidence of completed engineer, native Windows/macOS or bench validation. No physical DUT or hardware execution is represented. Recording provenance and absent DUT/time-base information are preserved in each input. See [the checklist](checklist.md), [the PR stack](stack.md) and fixture README files for expected pass/fail, nested recording, legacy and invalid-import cases.

Check out the published `feat/authoring-expert-commands` branch (the exact final head is recorded in its PR), then build and run from the repository root:

```sh
dotnet restore dirs.proj -p:RuntimeIdentifier=linux-x64 -p:Configuration=Release
dotnet build src/HardwareTest.Authoring/HardwareTest.Authoring.csproj -c Release -r linux-x64 --no-restore
dotnet run --project src/HardwareTest.Authoring/HardwareTest.Authoring.csproj -c Release -r linux-x64 --no-build
python tools/authoring/build-manual-review-bundle.py /tmp/authoring-review-inputs.zip
```

Use `win-x64` on Windows and `osx-arm64` on Apple Silicon. Open fixture workspace folders or import the recording files from `fixtures/`. Work on copies in a writable directory. The archive includes a SHA256 manifest and uses repository-relative paths; no binary ZIP is committed. Keep build output in a separate writable folder and inspect the generated build receipt there.

Open Commands with Ctrl+Shift+P (⌘+Shift+P on macOS). Search commands to see their prerequisites. Save uses Ctrl/⌘+S; Save all adds Shift. Normal text Undo/Redo stay with the text control; focus a non-text control to undo a document edit. F2 renames a step, Ctrl/⌘+D duplicates, Ctrl/⌘+Shift+A inserts the selected recipe, Alt+Up/Down moves and F8 opens the next editing issue or checked finding. Layout commands remember the local Programs rail, wide preview dock and Issues drawer; Reset restores defaults. Narrow screens always use the separate Preview route.

The external TUI escape hatch opens the saved compiled plan in a terminal using the selected installed OpenTAP home. Install the real TUI package and a terminal emulator first. It does not run the plan. Unsaved source edits stay open; on return external compiled changes become explicit reconciliation conflicts. Choose Import compiled content or Retain source before saving over those changes. On macOS launch `dotnet --roll-forward Major <home>/tap.dll tui <plan.TapPlan>` from a terminal and reopen/reconcile through the editor.

Final evidence is tracked in the PR. Prepared inputs, headless checks, real external TUI process observations and native desktop checks are reported separately. Windows/macOS native interaction and physical bench/engineer review require their actual environments.
