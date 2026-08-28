# Testing

UI/board tests stay separate from OpenTAP plan-behavior tests. Both share the OpenTAP session contract (aggregating `IOpenTapSession` and focused surfaces); Avalonia Feature ViewModels inject the narrow surfaces from [Phase 14](platform-phases/phase-14-session-facade-split.md).

| Suite | Purpose | OpenTAP | Emulation |
| --- | --- | --- | --- |
| Architecture | Layering smoke (Avalonia/OpenTAP boundaries, single Window, `AppJsonContext`, plugin source must not use `Ivi.Visa`, no static `StepRuntime` pause/interaction) | Load assemblies only | None |
| Session contracts | Shared `IOpenTapSession` behavior (real + fake) | Real or `FakeOpenTapSession` | MockDmm / canned trees |
| ViewModels | Run board / session / filters / rollup UX | `FakeOpenTapSession` (in-memory trees + optional recording replay) | No real instruments |
| Core/OpenTAP host | Plan load, hierarchy, Run Selected mask, SafeShutdown, progress/samples | Real in-process `OpenTapSession` (documented test-only host) | `MockDmmInstrument` |
| Avalonia E2E | Shell wiring only (DUT → Run → Results/Inspect) | Worker-backed session (`OpenTapWorkerClient`) | MockDmm + `UseMockVisa` |

CI runs Deno tasks from [`tools/ci/`](../tools/ci/) on **windows-latest** (required E2E) and **ubuntu-latest** (`linux-x64`; E2E advisory — the step is named **E2E smoke (advisory on Linux)**). Host tests run **without Coverlet**; `coverage` collects Core-safe tests only. See [containers.md](containers.md).

Platform roadmap (interactions, parameters, mixins): [opentap-platform.md](opentap-platform.md).
Hardening roadmap (gates, config, crash, CI): [platform-roadmap.md](platform-roadmap.md).

Build/version coverage (`BuildInfo`, `AppVersion` on `TestRunRecord`, Settings **Copy diagnostics**, `--version` parsing) lives in Core + ViewModels tests — see [phase-4-build-info.md](platform-phases/phase-4-build-info.md).
Schema gates and golden files under `tests/fixtures/schema/` are covered in Core tests — see [phase-5-schema-versioning.md](platform-phases/phase-5-schema-versioning.md).
Crash dossiers (writer, ring sink, redaction, dangling-run reconciliation) live in Core tests under `Crash/` — see [phase-6-crash-reporting.md](platform-phases/phase-6-crash-reporting.md).
Local CI tasks, coverage floors (TypeScript), and container rails — see [containers.md](containers.md) and [phase-7-containers-local-ci.md](platform-phases/phase-7-containers-local-ci.md).
Session contract tests (`HardwareTest.Session.Contracts`) run against both real and fake `IOpenTapSession` via the host and ViewModel suites — see [phase-8-session-contract-tests.md](platform-phases/phase-8-session-contract-tests.md).
Export targets, run retention, and free-space gates live in Core tests under `Storage/` — see [phase-10-export-storage-chrome.md](platform-phases/phase-10-export-storage-chrome.md).
Idle/stale and retention must use an injected `IClock` (`FakeClock` in tests), not `DateTimeOffset.UtcNow`. Clock-skew detector tests live under `Time/` — see [phase-25-clock-discipline.md](platform-phases/phase-25-clock-discipline.md).

## When to add which test

### Session contract (both implementations)

Put an assertion in `OpenTapSessionContractTests` only when it must hold for **both** the real session and `FakeOpenTapSession` (load/run/abort/pause/parameters/catalog). Implementation-specific edges (fake-only `BeginInteraction` / `LoadTreeFromNodes`, real instrument timing) stay in the host or ViewModel suite. Changing `IOpenTapSession` or a focused surface requires updating the matching `*.approved.txt` snapshot(s) in the same commit.

### Architecture (layering smoke)

Put a rule here only when it is a short, stable layering claim already written in README / platform docs (e.g. "Core must not reference Avalonia"). Failure messages must name the rule and the doc. Behavioral coverage (plan runs, ViewModel flow, E2E) stays in the suites below — see [phase-2-architecture-tests.md](platform-phases/phase-2-architecture-tests.md). Plugin VISA must go through Core `IVisaBroker`; `ArchitectureRulesTests.Plugin_source_must_not_use_Ivi_Visa` scans `Plugins.Basic` / `Plugins.Mixins` source and csproj — see [phase-22-visa-broker.md](platform-phases/phase-22-visa-broker.md). Pause/interaction must not be process-global statics; `ArchitectureRulesTests.StepRuntime_must_not_expose_static_pause_or_interaction` scans `src/` — see [phase-24-session-decomposition.md](platform-phases/phase-24-session-decomposition.md). Idle/retention/run-complete production paths must not call `DateTime.UtcNow` / `DateTimeOffset.UtcNow`; Safety Stop / worker kill must not wait on NTP — `ArchitectureRulesTests.Idle_retention_and_run_complete_must_not_use_wall_clock_UtcNow` and `Safety_stop_and_worker_kill_must_not_wait_on_NTP` — see [phase-25-clock-discipline.md](platform-phases/phase-25-clock-discipline.md).

### UI / board (ViewModels)

1. Build or load a tree via `FakeOpenTapSession` (`LoadSampleProgramAsync`, `LoadBoardDemoProgramAsync`, Fake-only `LoadPlanShapeAsync` / `LoadTreeFromNodes`).
2. Drive `RunTestViewModel` / `InspectViewModel` and assert StepRows, rollup chips, filters, or Inspect parity.
3. For a captured edge case offline: `ReplayRecording(dir, "cassette-name")` then refresh hierarchy/Inspect.

#### Run board child ViewModels (coordinator + owned children)

`RunTestViewModel` is a coordinator that owns one child ViewModel per panel (`StepDetail`, `Interaction`, `SessionPanel`, `ProgramSelection`, `StationOverrides`, `Live`, `StepTree`, `Run`). Children are constructed by the parent — not registered in DI — and receive services plus small `Func`/`Action` callbacks instead of a back-reference to the parent. The run pipeline takes the coordinator through `IRunBoardHost` so a stub can replace it. The UI flush pump (`IngestProgress`, `UiScheduler`, `RunOnUiAsync`) stays on the coordinator, which keeps every child dispatcher-agnostic. Feature files are capped at 600 lines by `ArchitectureRulesTests.Feature_source_files_stay_under_line_budget`; split into another child or a partial rather than raising the cap.

Pick the narrowest suite for what you are asserting:

- **One panel's own behavior** → construct the child directly with fakes/no-op callbacks, as in `RunBoardChildViewModelTests`. No dispatcher and no parent needed.
- **Cross-panel coordination** (a selection change refreshing detail, hero, plot and overrides together) → build the whole `RunTestViewModel`, set `UiScheduler = action => action()`, and assert through child paths such as `vm.StepTree.SelectedStep` or `vm.StepDetail.DetailChipText`.
- **AXAML bindings** use the same child paths (`{Binding StepDetail.DetailLines}`), so a test written against the child path matches what the view binds to.
- **Operator chrome / a11y (Phase 21)** — type floor, compact Pause/Stop captions, live regions, and Settings headings live in `Phase21OperatorChromeTests`. Do not announce plot-sample floods; Engineer/debug tables may stay tighter than the Run operational floor.
- **Operator prompt / session (900×600)** — Continue stays docked outside `PromptBodyScroller` (`InteractionHostView`); session Enter confirms DUT; typed fields bind `TwoWay` + `PropertyChanged`. Contracts live in `OperatorPromptChromeTests`; control-level bind/focus is in E2E (`RunFlowE2ETests`). Checklist: Continue visible without scrolling the prompt body; Enter confirms session; setting `DutSerialBox.Text` updates `SessionPanel.DutSerialInput`.
- **Operator vs engineer nav** — default (engineer mode off) left nav is Home / Run / Results / Settings. Inspect and Instruments appear after saving Engineer / debug mode (presentation, not auth). Report Preview is contextual from Results (`ShellNavigationPolicyTests`, `MainWindowViewModelTests`). Compact Run board (overview sidebar hidden, stage ComboBox, wrapped toolbars) is `IsCompactLayout` below `ShellLayoutBreakpoints.CompactBoardWidth`.
- **Guided commissioning** — Run blocks unbound / demo-only slots via `StationReadinessEvaluator` and deep-links to Instruments (`FocusProgram` + shell **Open Instruments**). Operators can remain on Instruments without a left-nav item. Program filter, Discover → Bind → *IDN? → Save stepper, and `station-idn.json` sidecar (not AppSettings) are covered by `StationReadinessEvaluatorTests`, `InstrumentsViewModelTests`, and `MainWindowViewModelTests`. Checklist: unbound Run does not start; Instruments opens on the blocking slot; *IDN? writes `station-idn.json`.
- **QA failure triage** — `RunTriageSummary.FromRecord` uses `StepAttempts` chronology (legacy `Steps` fallback). Opening a failed run sets list `ResultFilter` to Failed and defaults the detail pane to failed steps, with first-fail + attempt rollup. Date/operator filters and yield counts are on the Results list (`ResultsViewModelTests`, `RunTriageSummaryTests`).
- **Compare with previous** — Opening a run on Results compares channel means to the latest earlier run with the same DUT serial + plan (`RunComparisonService`, `EffectiveMetricKey`). Missing metrics are listed as unavailable. Schema-drift strip flags read-only future-schema runs on the list. Export packages include `diagnostics.txt` (support block + catalog self-check); Settings **Copy diagnostics** includes the same catalog check (`RunComparisonServiceTests`, `ResultsComparisonTests`, `ProgramCatalogTests`). Do not block Run on comparison failures.

### Plan behavior (OpenTAP host)

1. Prefer a C# factory in `PlanShapeFixtures` / `SampleProgramFactory` / `BoardDemoProgramFactory` / `SweepDemoProgramFactory` (optionally `SaveBeside` under `plans/opentap/fixtures/`).
2. Load with concrete `OpenTapSession.LoadPlanShapeAsync(...)` (not on `IOpenTapSession`) or the sample / board-demo / sweep-demo loaders. The in-process `OpenTapSession` is the **documented test-only host** for the serial `OpenTapSerial` suite — it does not pass a cancel token into `Execute` so Abort cannot poison `TapThread`.
3. Assert `StepTree` shape, unique paths, Run Selected enable-mask behavior, or SafeShutdown presence.
4. Keep in-process host tests that call `TestPlan.Execute` in the `OpenTapSerial` collection (`DisableParallelization`). Serial is required because TapThread / PluginManager are still process-global — not because of `StepRuntime` statics (those are gone).
5. **Abort isolation:** Host `Abort` cancels cooperatively via CTS + `WaitIfPaused` / interaction gates — it does **not** call `TapThread.Abort`. Prefer draining run tasks in `finally` after Abort in tests.
6. **Worker kill:** `OpenTapWorkerKillTests` loads `HangForeverStep` through `OpenTapWorkerClient` (not `IOpenTapSession`). Abort then kill-timeout must run `ISafetyController.SafeIdle` and leave the client able to start a second run. ViewModels stay on `FakeOpenTapSession`. The UI process uses the worker via Composition.
7. **Run context isolation:** `OpenTapRunContextTests` may run in parallel. Two `OpenTapRunContext` / `IStepRuntime` instances must not share pause or interaction. Do not require `TestPlan.Execute` for that proof. Pause/Resume mutate the live control gate; `BeginRun` must not re-apply a snapshot (worker Run is background while Pause/Resume stay on the IPC loop).

Named templates live in `PlanDiagnosticsTests` (`PlanDiagnostics_*`).

See also [adapting.md](adapting.md) for productizing plans, plugins, and reports.
### Record and adapt (progress/summary cassette)

Not a full SCPI VCR. Capture what the board already consumes:

1. Wrap `IProgress<OpenTapProgress>` with `OpenTapRunRecorder`, run via `OpenTapSession`, then `WriteBeside(dir, baseName, summary)`.
2. Commit `*.progress.json` + `*.summary.json` under `tests/fixtures/opentap/recordings/`.
3. Assert offline with `OpenTapRunRecorder.LoadBeside` (host) or `FakeOpenTapSession.ReplayRecording` (ViewModels).

To regenerate the checked-in `sample-pass` cassette, build host tests with `/p:DefineConstants=RECORD_OPENTAP_RUN` and run `Record_sample_pass_cassette` (see `#if RECORD_OPENTAP_RUN` in `OpenTapRunRecorderTests`).

## Plan contract (Run board)

Host `PlanContractValidator` encodes these checks for TUI/Editor authors (`HardwareTest --validate-plan`, `HardwareTest.PlanValidate`). Warnings do not block operator Run.

- Prefer unique step paths (duplicate sibling names need path-qualified selection).
- Max useful nest depth for chrome is three levels (Stages → Sections → Nested); deeper nodes still appear as leaves under path (validator warns, does not fail).
- Include `SafeShutdownStep` when using Run Selected (selection keeps SafeShutdown enabled by default). Opt out with `selectionIncludesCleanup: false` in `{planId}.program.json` only when shutdown is suite-scoped and selection is software-only. Disabled siblings showing NotExecuted/Invalidated is expected — not “cleanup skipped.”
- Instruments must be extractable for the Instruments page (or document limits for foreign plugins).
- No OpenTAP `DialogStep` / OS dialogs; Presentation mixins should not be timeseries-only when the verdict is a band/threshold.
- Sidecar `{planId}.program.json` present (warning if missing) and valid JSON (error if not). Copy `plans/opentap/template.program.json`.

Coverage lives in `PlanContractValidatorTests` (OpenTapSerial) plus `ConfigurationArgs` parse for `--validate-plan`. Named shape templates remain in `PlanDiagnosticsTests` (`PlanDiagnostics_*`).

## Local commands

```bash
# Same tasks CI runs (RID defaults to the host):
deno task --cwd tools/ci all -- --rid win-x64

# Or individual suites (OpenTAP host + E2E share process-global TapThread state):
deno run -A tools/ci/main.ts test:arch --rid win-x64
deno run -A tools/ci/main.ts test:host --rid win-x64
deno run -A tools/ci/main.ts test:vm --rid win-x64
deno run -A tools/ci/main.ts test:e2e --rid win-x64
deno run -A tools/ci/main.ts coverage --rid win-x64
deno run -A tools/ci/main.ts audit

# Raw dotnet still works:
dotnet test dirs.proj -r win-x64 -m:1
```
