# Fresh-eyes review round 3 (findings → phases)

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Routed. Immediate correctness ships in [Phase 19](phase-19-immediate-correctness.md); larger work is numbered 20–25.
**Source:** Fresh-context review of `latest` @ `c476e32`, after platform Phases 1–18 and OpenTAP A–J (K still Planned)
**Predecessor:** [review-post-phase-15.md](review-post-phase-15.md) (round 2 — F1–F6 fixed; CI follow-ups still open)

Every finding below was **reproduced or read directly in code**. Round-2 items already marked Fixed are not re-litigated.

## Verification method

Run on `linux-x64` with .NET 10:

- `dotnet build dirs.proj -c Release -r linux-x64` — 0 warnings (`TreatWarningsAsErrors=true`)
- Architecture 15/15, ViewModels 201/201, E2E 9/9
- OpenTAP host 182/183 aggregate (flaky under Coverlet); 183/183 isolated
- Coverage floors: Core ~79% / Engine ~81% / Hardware ~86%
- `dotnet format dirs.proj --verify-no-changes` exits 0 without checking anything
- `dotnet format HardwareTest.slnx --verify-no-changes` reports a real diagnostic backlog (dominated by `RCS1139`)

## Finding → phase map

| ID | Finding | Phase |
| --- | --- | --- |
| R3-1 | Run search steals `f` and `/` while a TextBox has focus | **19** |
| R3-2 | `ReportPreview` / `Instruments` / `LoadSelectedProgramAsync` still mutate UI-bound state after `ConfigureAwait(false)` | **19** |
| R3-3 | `UiDispatch` drops dispatcher failures with `Debug.WriteLine` only | **19** |
| R3-4 | Typst PDFs use `File.WriteAllBytes` + `.GetAwaiter().GetResult()`; `ExportPackage` uses `Path.Combine` on staging | **19** |
| R3-5 | Extra OpenTAP plugin dirs load from any path; search is once-per-process | **19** |
| R3-6 | Settings debounce Save is fire-and-forget; faults only `Debug.WriteLine` | **19** |
| R3-7 | Operator “Safety Stop” copy implies a hardware interlock; abort is cooperative | **19** (copy) / **23** (real interlock + killable worker) |
| R3-8 | Chip contrast: Pending ~4.37:1, Awaiting ~3.08:1 on white 11px text | **19** |
| R3-9 | Settings checkboxes have no `Content`; inputs lack `LabeledBy` | **19** |
| R3-10 | `HardwareTest.slnx` omits Mixins, Architecture.Tests, Session.Contracts; format gate is inert | **19** |
| R3-11 | No lockfiles, floating Actions tags, no workflow permissions/timeouts/concurrency, Coverlet on host tests, Linux E2E advisory, `InformationalVersion` embeds `UtcNow` | **20** |
| R3-12 | Run board density, compact Pause/Stop tooltips not appearing, live regions | **21** |
| R3-13 | Dual VISA paths: Core `VisaSessionGate` vs plugin `Ivi.Visa.GlobalResourceManager.Open` | **22** |
| R3-14 | Stop cannot preempt third-party / blocking OpenTAP steps (`CancellationToken.None`); no `ISafetyController` | **23** |
| R3-15 | `OpenTapSession` ~1738 LOC god object; `StepRuntime` statics; host tests must be serial; Coverlet flakes | **24** (prerequisite for OpenTAP K) |
| R3-16 | Wall-clock idle/retention/run ordering; no `TimeProvider` | **25** (Done — `IClock` + skew strip) |

## Confirmed defects (round 3)

### R3-1 — Run search drops `f` and `/`

**Severity:** High · **Reproduced** · `RunTestView.OnKeyDown`

`Key.F`, `Key.Oem2`, and `Key.Divide` are handled even when a `TextBox` has focus. Typing `safe f/ voltage` into step search displayed `sae voltag`.

**Fix in 19:** ignore board shortcuts when the focused visual is an editable control; keep `/` and `Ctrl+F` for search, `F` / `Shift+F` for fail navigation only when the board (not a field) has focus.

### R3-2 — Off-UI-thread mutations still exist

**Severity:** High · **Read in code** · Results was fixed in round 2 (F2). Remaining:

- `ReportPreviewViewModel.LoadFromPathAsync` `finally` sets `IsBusy` after `ConfigureAwait(false)`; `LoadLatestAsync` sets `Status` after store I/O
- `InstrumentsViewModel` sets `IsBusy` / `Status` in `finally` / `catch` after awaits
- `RunTestViewModel.LoadSelectedProgramAsync` awaits `LoadProgramEntryAsync(...).ConfigureAwait(false)` then mutates `ObservableCollection`s via `StepTree.RebuildFromHost`

**Fix in 19:** compute-then-apply through `UiDispatch` / `UiScheduler` (same seam as Results). Do not introduce a second dispatcher abstraction in this phase.

### R3-3 — `UiDispatch` silent drop

**Severity:** Medium · **Read in code**

When the dispatcher throws, the action is not run (correct — must not mutate off-thread) but the only record is `Debug.WriteLine`.

**Fix in 19:** `Log.Warning` (Serilog) with exception details; still no-op the action.

### R3-4 — PDF / export persistence gaps

**Severity:** High · **Read in code**

Settings and run JSON use `AtomicFile`. Report PDFs still `File.WriteAllBytes` inside `Task.Run`, and `SaveAsync` is blocked with `.GetAwaiter().GetResult()`. `ExportPackage` sanitizes `..` then `Path.Combine(staging, relative)` instead of `PathContainment.CombineUnderRoot`.

**Fix in 19:** `AtomicFile.WriteAllBytesAsync` + awaited `SaveAsync`; `CombineUnderRoot` on every export dest. Crash dossiers / OpenTAP recordings stay Phase 20-adjacent (not in 19).

### R3-5 — Plugin directories are not a trust boundary

**Severity:** High · **Read in code** · `OpenTapSession.EnsurePlugins`

Any `OpenTapPluginDirectories` / `HARDWARETEST_OPENTAP_PLUGIN_DIRS` path is added to `PluginManager.DirectoriesToSearch` and loaded into the process. Search is once-per-session (`_pluginSearchDone`); settings changes after first search need a restart and that is not advertised well.

**Fix in 19:** load extras only under `{DataDirectory}/plugins` unless Engineer debug is on; skip + log escapes; Settings copy that plugin-dir changes require restart. No fake hot-reload.

### R3-6 — Settings debounce faults are invisible

**Severity:** Medium · **Read in code**

The 400ms timer fire-and-forget `SaveAsync` continuation writes faults to `Debug.WriteLine`.

**Fix in 19:** marshal the fault onto `Status` (and keep the command-path `Save` as the explicit write operators already have).

### R3-7 — “Safety Stop” is a software abort

**Severity:** High · **Read in code + docs**

`OpenTapSession.Abort()` cancels a CTS and interaction gates. `plan.Execute` is called with `CancellationToken.None`. `TapThread.Abort` is deliberately avoided because it poisons in-process OpenTAP. `VisaDmmInstrument` used to open IVI directly, so Core `VisaSessionGate` preemption did not cover plan I/O.

**Fix in 19:** operator-facing **Stop Run** copy that states this is a cooperative software stop, not a hardware interlock. **Fix in 22:** plan VISA goes through `IVisaBroker` / `VisaSessionGate`. **Fix in 23:** `ISafetyController` (no-op is “Not wired”), killable OpenTAP worker, `SafeIdle` before kill/bookkeeping. Do not use `TapThread.Abort` in the UI process. A real ESTOP adapter remains a follow-up.

### R3-8 / R3-9 — Contrast and Settings names

**Severity:** Medium · **Reproduced** (contrast) / **Read** (AXAML)

White-on-`#607D8B` Pending ~4.37:1; Awaiting `#EF6C00` ~3.08:1. Settings uses adjacent `TextBlock`s instead of checkbox `Content` / `LabeledBy`.

**Fix in 19:** darker chip backgrounds with WCAG AA math tests; checkbox `Content` and `LabeledBy`/`Name` on inputs. Run density / live regions stay Phase 21.

### R3-10 — Solution + format gate

**Severity:** Medium · **Reproduced**

`HardwareTest.slnx` omits Mixins, Architecture.Tests, and Session.Contracts (architecture tests locate the repo by walking to this file). CI formats `dirs.proj`, which cannot be formatted and exits 0.

**Fix in 19:** add the missing projects; suppress `RCS1139` (intentional one-line `///`); point the gate at `HardwareTest.slnx` and make it blocking once remaining diagnostics are zero.

## Larger work (do not implement in 19)

See phases [20](phase-20-ci-honesty.md)–[25](phase-25-clock-discipline.md). OpenTAP letter track is unchanged: keep public `IOpenTap*` surfaces. [Phase 24](phase-24-session-decomposition.md) landed R3-15 (`OpenTapRunContext` + `IStepRuntime`; `TestPlan.Execute` stays serial because TapThread / PluginManager are process-global). Phase 24 remains the structural prerequisite for [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md). [Phase 25](phase-25-clock-discipline.md) landed R3-16 (`IClock`, last-known-good / optional NTP skew warning; Run is not blocked).

## Out of this map

- Schema migration engine stays [deferred](../deferred/deferred-schema-migration.md) until the first real schema bump (migrator-on-`JsonNode` approach is sketched there).
- `StubRunComparisonService` stays [deferred](../deferred/deferred-run-comparison.md) — do not register it as an operator feature until implemented.
- Appliance kiosk, package feed install, bench profile UI, localization, auto-update, remote crash upload remain deferred.
