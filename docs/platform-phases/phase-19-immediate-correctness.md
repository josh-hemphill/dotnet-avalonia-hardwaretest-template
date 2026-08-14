# Phase 19 — Immediate correctness

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 18](phase-18-operator-touch-density.md), round-2 F1–F6
**Unblocks:** [Phase 20](phase-20-ci-honesty.md) (format gate is a 19 deliverable; lockfiles/pins stay 20), [Phase 21](phase-21-operator-chrome-a11y.md)
**Status:** Done
**Also absorbs:** Round-3 contained defects R3-1–R3-10 from [review-round-3.md](review-round-3.md)

## Goal

Land the **contained, in-process** correctness fixes from the round-3 review without starting VISA unification, the OpenTAP worker, or session decomposition. Operators must be able to type into search, UI-bound state must stay on the UI thread, persistence/export must not escape roots, extra plugins must be a trust boundary, and “Stop” copy must not claim a hardware interlock.

## Why this exists

Phases 1–18 made the shell supportable. Round 3 still found operator-visible bugs (search swallowing `f`/`/`), remaining off-UI-thread mutations after the Results fix, PDF writes that are not atomic, plugin paths loaded from anywhere, and a format gate that never ran. Those are small enough to ship now. Dual VISA, killable OpenTAP, and the 1738-line session façade are not.

## Locked decisions

- **No `TapThread.Abort`.** Software stop stays cooperative (CTS + interaction gates). Rename the operator action to **Stop Run**; keep existing command identifiers (`SafetyStopCommand`, `RequestSafetyStop`, `Abort(safetyStop:)`).
- **One dispatcher seam.** Reuse `UiDispatch` + view-model `UiScheduler`. Do not add `IUiDispatcher` in this phase.
- **Plugin extras** load only from `{DataDirectory}/plugins` unless Engineer debug is on. Escapes are skipped and logged. Changing plugin directories requires an application restart — no fake hot-reload.
- **Keyboard:** ignore Run-board shortcuts when focus is a text input (`TextBox`, `AutoCompleteBox`, `NumericUpDown`, `ComboBox`). `/` and `Ctrl+F` focus search; `F` / `Shift+F` are fail navigation only when the board has focus.
- **Format:** suppress `RCS1139` (one-line `///` is intentional). Point CI at `HardwareTest.slnx`. Remaining diagnostics must be zero so the gate can block.
- **Feature line budget** stays 600. Do not grow `OpenTapSession` or `RunTestViewModel` to absorb this work.
- **No new OS dialogs / second windows.**

## Workstreams

### A — Keyboard

- `RunTestView.OnKeyDown` skips shortcuts when the focused visual is editable.
- Extract a small mapper so ViewModel tests can cover editable vs board focus without driving the full view.

### B — UI thread

- `LoadSelectedProgramAsync`: marshal `RebuildFromHost` / station-override refreshes after `ConfigureAwait(false)`.
- `ReportPreviewViewModel`: every post-await mutation (`IsBusy`, `Status`, `Pages`) through `UiDispatch.RunAsync` / `UiScheduler`.
- `InstrumentsViewModel`: marshal `IsBusy` / `Status` in `catch`/`finally`.
- `UiDispatch`: Serilog `Log.Warning` when dropping an update; still do not run the action off-thread.

### C — Persistence & plugins

- `TypstReportService.GenerateReportsAsync`: `AtomicFile.WriteAllBytesAsync` then `await _runStore.SaveAsync` (no `.GetResult()`).
- `ExportTargetService.ExportPackage`: `PathContainment.CombineUnderRoot` for staging dests.
- `OpenTapSession.EnsurePlugins`: filter extras with `PathContainment` under `{DataDirectory}/plugins` unless Engineer debug.
- Settings debounce faults → `Status` (not `Debug.WriteLine` only).
- Settings plugin section states restart-required and the trusted root.

### D — Operator honesty & a11y (contained)

- Button/tooltip/status copy: **Stop Run**; tooltip states cooperative software stop, not a hardware interlock.
- Darker Pending / Awaiting chip backgrounds; unit-test WCAG contrast vs white chip text.
- Settings: checkbox `Content`; `AutomationProperties.LabeledBy` and/or `Name` on text/combo inputs.

### E — Solution & format gate

- Add Mixins, Architecture.Tests, and Session.Contracts to `HardwareTest.slnx`.
- `.editorconfig`: `dotnet_diagnostic.RCS1139.severity = none`.
- CI: `dotnet format HardwareTest.slnx --verify-no-changes --no-restore` on Windows and Linux; remove `continue-on-error` once verify is clean.

## Exit criteria

- [x] Typing `safe f/ voltage` into Run step search keeps every character (shortcuts ignored while the search box has focus; mapper tests)
- [x] Report preview / Instruments / program load mutate UI-bound properties only via `UiScheduler` in tests
- [x] PDF write is atomic; export `..` relative names stay under the package root
- [x] Untrusted plugin dirs are not added to `PluginManager.DirectoriesToSearch` (Engineer debug still can)
- [x] Debounced save faults surface on Settings `Status`
- [x] Operator chrome says Stop Run and chips meet WCAG AA vs white
- [x] `dotnet format HardwareTest.slnx --verify-no-changes` is a blocking CI step
- [x] Architecture / ViewModels / E2E / host (without Coverlet) green; Feature line budgets pass

## Out of scope

- VISA broker / `IBenchOperationCoordinator` ([Phase 22](phase-22-visa-broker.md))
- `ISafetyController` and OpenTAP worker process ([Phase 23](phase-23-safety-opentap-worker.md))
- `OpenTapSession` split / `StepRuntime` statics ([Phase 24](phase-24-session-decomposition.md))
- Lockfiles, action SHA pins, coverage split, Linux E2E blocking ([Phase 20](phase-20-ci-honesty.md))
- Run density, live regions, disabled-control tooltip chrome ([Phase 21](phase-21-operator-chrome-a11y.md))
- Crash-dossier / OpenTAP-recording atomic writes (follow with 20 if still open)
- Schema migration, run comparison, appliance kiosk (deferred)

## Related

- [review-round-3.md](review-round-3.md)
- Round 2 F2/F5/F6 in [review-post-phase-15.md](review-post-phase-15.md)
